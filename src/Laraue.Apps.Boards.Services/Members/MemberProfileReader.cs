using Laraue.Apps.Boards.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.Members;

/// <summary>
/// Fills in how people are shown in an organization (<see cref="MemberProfile"/>) on DTOs showing a
/// person (<see cref="IEnrichableUser"/>): queries project just the user ids, and this enriches the whole
/// page with one query, instead of every query joining the membership table itself.
/// Includes former members (<c>OrganizationUser.LeftAt</c>) - their issues, comments and history still
/// show their name; a user with no membership row gets <see cref="MemberProfile.Unknown"/>.
/// A plain read with no permission checks, like <see cref="History.OrganizationHistoryReader"/>: callers
/// check access first.
/// </summary>
public interface IMemberProfileReader
{
    /// <summary>Enriches every user with their profile in the organization.</summary>
    Task EnrichUsers(
        long organizationId,
        IEnumerable<IEnrichableUser> users,
        CancellationToken cancellationToken);

    /// <summary>
    /// Same, for items from several organizations at once (e.g. search results spanning every
    /// organization the caller can read) - each item's user is shown the way they are in that item's
    /// organization. Items whose <paramref name="user"/> is null are skipped.
    /// </summary>
    Task EnrichUsers<T>(
        IEnumerable<T> items,
        Func<T, long> organizationId,
        Func<T, IEnrichableUser?> user,
        CancellationToken cancellationToken);
}

public class MemberProfileReader(DatabaseContext context) : IMemberProfileReader
{
    public Task EnrichUsers(
        long organizationId,
        IEnumerable<IEnrichableUser> users,
        CancellationToken cancellationToken)
    {
        return EnrichUsers(users, _ => organizationId, user => user, cancellationToken);
    }

    public async Task EnrichUsers<T>(
        IEnumerable<T> items,
        Func<T, long> organizationId,
        Func<T, IEnrichableUser?> user,
        CancellationToken cancellationToken)
    {
        var people = items
            .Select(item => (User: user(item), OrganizationId: organizationId(item)))
            .Where(x => x.User is not null)
            .Select(x => (User: x.User!, Key: new OrganizationMember(x.OrganizationId, x.User!.UserId)))
            .ToArray();

        if (people.Length == 0)
            return;

        var profiles = await GetProfilesAsync(people.Select(x => x.Key).ToHashSet(), cancellationToken);

        foreach (var person in people)
        {
            var profile = profiles.GetValueOrDefault(person.Key, MemberProfile.Unknown);

            person.User.DisplayName = profile.DisplayName;
            person.User.Initials = profile.Initials;
            person.User.Color = profile.Color;
        }
    }

    private async Task<Dictionary<OrganizationMember, MemberProfile>> GetProfilesAsync(
        HashSet<OrganizationMember> keys,
        CancellationToken cancellationToken)
    {
        var organizationIds = keys.Select(x => x.OrganizationId).Distinct().ToArray();
        var userIds = keys.Select(x => x.UserId).Distinct().ToArray();

        // Organizations x users can over-fetch a few rows when several organizations are asked for;
        // only the requested pairs are kept below.
        var rows = await context.OrganizationUsers
            .Where(x => organizationIds.Contains(x.OrganizationId) && userIds.Contains(x.UserId))
            .Select(x => new
            {
                x.OrganizationId,
                x.UserId,
                x.DisplayName,
                x.Initials,
                x.Color,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => (Key: new OrganizationMember(x.OrganizationId, x.UserId), Profile: new MemberProfile(x.DisplayName, x.Initials, x.Color)))
            .Where(x => keys.Contains(x.Key))
            .DistinctBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Profile);
    }

    private readonly record struct OrganizationMember(long OrganizationId, Guid UserId);
}
