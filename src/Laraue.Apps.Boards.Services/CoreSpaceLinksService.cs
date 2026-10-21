using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.Resources;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// A space's links to things outside Boards (<see cref="SpaceLink"/>, BRD-312), saved as a whole with the space's
/// settings. No permission checks, like every core service: the caller checks that the person may update the space.
/// </summary>
public interface ICoreSpaceLinksService
{
    /// <summary>
    /// Makes <paramref name="links"/> the space's links: those with an id are kept (and changed), new ones added, the
    /// rest removed. <see cref="BadRequestException"/>, with every problem as a field error, when an id isn't one of the
    /// space's links, a URL is listed twice, or there are more than <see cref="Constraints.MaxSpaceLinksPerSpace"/>.
    /// The caller must have opened a transaction, so a failure changes nothing.
    /// </summary>
    Task Replace(
        long spaceId,
        IReadOnlyList<SpaceLinkChange> links,
        Guid userId,
        CancellationToken cancellationToken);
}

/// <param name="Id">The existing link's id, or null for a new link.</param>
public sealed record SpaceLinkChange(long? Id, string Url, string Description);

public class CoreSpaceLinksService(
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider)
    : ICoreSpaceLinksService
{
    public async Task Replace(
        long spaceId,
        IReadOnlyList<SpaceLinkChange> links,
        Guid userId,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var existing = await context.SpaceLinks
            .Where(x => x.SpaceId == spaceId)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        Validate(links, existing);

        var keptIds = links
            .Select(x => x.Id)
            .OfType<long>()
            .ToHashSet();
        await context.SpaceLinks
            .Where(x => x.SpaceId == spaceId && !keptIds.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);

        // Two existing links may swap URLs, which the unique (space, URL) index would refuse half-way: changed URLs
        // are parked on a unique placeholder first, then set.
        var changedUrls = links
            .Where(x => x.Id is { } id && existing[id].Url != x.Url)
            .ToList();
        foreach (var link in changedUrls)
        {
            await context.SpaceLinks
                .Where(x => x.Id == link.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.Url, $"pending:{link.Id}"),
                    cancellationToken);
        }

        foreach (var link in links.Where(x => x.Id is { } id && IsChanged(existing[id], x)))
        {
            await context.SpaceLinks
                .Where(x => x.Id == link.Id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.Url, link.Url)
                        .SetProperty(x => x.Description, link.Description),
                    cancellationToken);
        }

        var now = dateTimeProvider.UtcNow;
        context.SpaceLinks.AddRange(links
            .Where(x => x.Id is null)
            .Select(x => new SpaceLink
            {
                SpaceId = spaceId,
                Url = x.Url,
                Description = x.Description,
                CreatedAt = now,
                CreatedByUserId = userId,
            }));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void Validate(IReadOnlyList<SpaceLinkChange> links, Dictionary<long, SpaceLink> existing)
    {
        var errors = new Dictionary<string, string?[]>();
        if (links.Count > Constraints.MaxSpaceLinksPerSpace)
        {
            errors["links"] = [string.Format(ErrorMessages.SpaceLinksLimitReached, Constraints.MaxSpaceLinksPerSpace)];
        }

        var seenUrls = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < links.Count; i++)
        {
            if (links[i].Id is { } id && !existing.ContainsKey(id))
            {
                errors[$"links[{i}].id"] = [ErrorMessages.SpaceLinkNotFound];
            }

            if (!seenUrls.Add(links[i].Url))
            {
                errors[$"links[{i}].url"] = [ErrorMessages.SpaceLinkUrlAlreadyAdded];
            }
        }

        if (errors.Count > 0)
        {
            throw new BadRequestException(errors);
        }
    }

    private static bool IsChanged(SpaceLink saved, SpaceLinkChange link)
    {
        return saved.Url != link.Url || saved.Description != link.Description;
    }
}
