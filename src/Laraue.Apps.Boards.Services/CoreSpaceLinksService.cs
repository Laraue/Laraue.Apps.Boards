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
    /// Makes <paramref name="links"/> the space's links, in that order. A link whose URL the space already had keeps
    /// when and by whom it was added. <see cref="BadRequestException"/>, with every problem as a field error, when a URL
    /// is listed twice or there are more than <see cref="Constraints.MaxSpaceLinksPerSpace"/>. The caller must have
    /// opened a transaction, so a failure changes nothing.
    /// </summary>
    Task Replace(
        long spaceId,
        IReadOnlyList<SpaceLinkChange> links,
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed record SpaceLinkChange(string Url, string Description);

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
        Validate(links);

        var added = await context.SpaceLinks
            .Where(x => x.SpaceId == spaceId)
            .ToDictionaryAsync(
                x => x.Url,
                x => new LinkAdded(x.CreatedAt, x.CreatedByUserId),
                cancellationToken);

        // Rewritten as a whole: the list is short, and nothing refers to a link's id.
        await context.SpaceLinks
            .Where(x => x.SpaceId == spaceId)
            .ExecuteDeleteAsync(cancellationToken);

        var now = new LinkAdded(dateTimeProvider.UtcNow, userId);
        context.SpaceLinks.AddRange(links
            .Select(x =>
            {
                var linkAdded = added.GetValueOrDefault(x.Url, now);
                return new SpaceLink
                {
                    SpaceId = spaceId,
                    Url = x.Url,
                    Description = x.Description,
                    CreatedAt = linkAdded.At,
                    CreatedByUserId = linkAdded.ByUserId,
                };
            }));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void Validate(IReadOnlyList<SpaceLinkChange> links)
    {
        var errors = new Dictionary<string, string?[]>();
        if (links.Count > Constraints.MaxSpaceLinksPerSpace)
        {
            errors["links"] = [string.Format(ErrorMessages.SpaceLinksLimitReached, Constraints.MaxSpaceLinksPerSpace)];
        }

        var seenUrls = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < links.Count; i++)
        {
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

    private sealed record LinkAdded(DateTime At, Guid ByUserId);
}
