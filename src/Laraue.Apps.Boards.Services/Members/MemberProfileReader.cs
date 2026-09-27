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
    /// Same, for users from several organizations at once (e.g. search results spanning every
    /// organization the caller can read) - each user is shown the way they are in their item's
    /// organization.
    /// </summary>
    Task EnrichUsers<T>(
        IEnumerable<T> users,
        Func<T, long> organizationId,
        CancellationToken cancellationToken)
        where T : IEnrichableUser;
}

public class MemberProfileReader(DatabaseContext context) : IMemberProfileReader
{
    public Task EnrichUsers(
        long organizationId,
        IEnumerable<IEnrichableUser> users,
        CancellationToken cancellationToken)
    {
        return EnrichUsers(users, _ => organizationId, cancellationToken);
    }

    public async Task EnrichUsers<T>(
        IEnumerable<T> users,
        Func<T, long> organizationId,
        CancellationToken cancellationToken)
        where T : IEnrichableUser
    {
        var people = users
            .Select(user => (User: user, Key: new OrganizationMember(organizationId(user), user.UserId)))
            .ToArray();

        if (people.Length == 0)
            return;

        var profiles = await GetProfilesAsync(people.Select(x => x.Key).ToHashSet(), cancellationToken);

        foreach (var person in people)
            person.User.Enrich(profiles.GetValueOrDefault(person.Key, MemberProfile.Unknown));
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
