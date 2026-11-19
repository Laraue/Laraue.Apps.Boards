using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices.Resources;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.TelegramServices.Services.GroupChats;

/// <summary>
/// Resolves a chat destination - <see cref="LinkDestinationType"/> + id - into the organization it belongs to and the
/// names to show. No permission checks: callers check access to <see cref="LinkDestination.OrganizationId"/>.
/// </summary>
public interface ILinkDestinationReader
{
    /// <param name="includeDeleted">True to resolve a soft-deleted target too (a linked chat's settings still show
    /// it, so it can be relinked or unlinked); false for a destination being picked now.</param>
    /// <returns>Null when nothing with that id exists (or it's deleted and <paramref name="includeDeleted"/> is
    /// false).</returns>
    Task<LinkDestination?> Load(
        LinkDestinationType type,
        long id,
        bool includeDeleted,
        CancellationToken cancellationToken);
}

public class LinkDestinationReader(DatabaseContext context) : ILinkDestinationReader
{
    public Task<LinkDestination?> Load(
        LinkDestinationType type,
        long id,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        return type switch
        {
            LinkDestinationType.Organization => (includeDeleted ? context.Organizations : context.ActiveOrganizations())
                .Where(x => x.Id == id)
                .Select(x => new LinkDestination
                {
                    Type = type,
                    Id = x.Id,
                    OrganizationId = x.Id,
                    OrganizationName = x.Name,
                    IsDeleted = x.DeletedAt != null,
                })
                .FirstOrDefaultAsyncEF(cancellationToken),

            LinkDestinationType.Space => (includeDeleted ? context.Spaces : context.ActiveSpaces())
                .Where(x => x.Id == id)
                .Select(x => new LinkDestination
                {
                    Type = type,
                    Id = x.Id,
                    OrganizationId = x.OrganizationId,
                    OrganizationName = x.Organization!.Name,
                    SpaceId = x.Id,
                    SpaceName = x.Name,
                    IsDeleted = x.DeletedAt != null,
                })
                .FirstOrDefaultAsyncEF(cancellationToken),

            LinkDestinationType.Epic => (includeDeleted ? context.Epics : context.ActiveEpics())
                .Where(x => x.Id == id)
                .Select(x => new LinkDestination
                {
                    Type = type,
                    Id = x.Id,
                    OrganizationId = x.Space!.OrganizationId,
                    OrganizationName = x.Space.Organization!.Name,
                    SpaceId = x.SpaceId,
                    SpaceName = x.Space.Name,
                    EpicId = x.Id,
                    EpicName = x.Name,
                    IsDeleted = x.DeletedAt != null,
                })
                .FirstOrDefaultAsyncEF(cancellationToken),

            LinkDestinationType.Status => (includeDeleted ? context.Statuses : context.ActiveStatuses())
                .Where(x => x.Id == id)
                .Select(x => new LinkDestination
                {
                    Type = type,
                    Id = x.Id,
                    OrganizationId = x.Epic!.Space!.OrganizationId,
                    OrganizationName = x.Epic.Space.Organization!.Name,
                    SpaceId = x.Epic.SpaceId,
                    SpaceName = x.Epic.Space.Name,
                    EpicId = x.EpicId,
                    EpicName = x.Epic.Name,
                    // A backlog epic has one status, never picked by name, so it isn't shown.
                    StatusName = x.Epic.IsDefault ? null : x.Name,
                    IsDeleted = x.DeletedAt != null,
                })
                .FirstOrDefaultAsyncEF(cancellationToken),

            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}

/// <summary>
/// Where a chat saves to: fixed down to <see cref="Type"/>, the levels below chosen each time.
/// </summary>
public sealed record LinkDestination
{
    public required LinkDestinationType Type { get; init; }

    /// <summary>
    /// The id of the <see cref="Type"/> entity.
    /// </summary>
    public required long Id { get; init; }

    public required long OrganizationId { get; init; }
    public required string OrganizationName { get; init; }
    public long? SpaceId { get; init; }
    public string? SpaceName { get; init; }
    public long? EpicId { get; init; }
    public string? EpicName { get; init; }
    public string? StatusName { get; init; }

    /// <summary>
    /// The <see cref="Type"/> entity was soft-deleted. Deleting a level soft-deletes everything below it, so the
    /// fixed entity itself is enough to check.
    /// </summary>
    public required bool IsDeleted { get; init; }

    /// <summary>
    /// "Org → Space → Epic → Status", ending with "❓ chosen each time" when something is left to choose.
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string> { OrganizationName };

        if (SpaceName is not null)
            parts.Add(SpaceName);

        if (EpicName is not null)
            parts.Add(EpicName);

        if (StatusName is not null)
            parts.Add(StatusName);

        if (Type != LinkDestinationType.Status)
            parts.Add(Phrases.LinkDestinationChosenEachTime);

        return string.Join(" → ", parts);
    }
}
