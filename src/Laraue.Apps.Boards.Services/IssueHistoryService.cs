using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Persists <see cref="OrganizationLogItem"/>s (built via <see cref="IOrganizationLogItemFactory"/>)
/// as an <see cref="OrganizationLog"/> row. Used by core services (<see cref="CoreIssuesService"/>
/// and friends) so history-logging isn't duplicated across every entity mutation.
/// </summary>
public interface IIssueHistoryService
{
    /// <summary>
    /// Persists an <see cref="OrganizationLog"/> row for the given entity, regardless of whether
    /// <paramref name="items"/> is empty - use this for actions that are notable on their own
    /// (create/delete), where an empty item list can still be meaningful.
    /// </summary>
    Task Record(
        long entityId,
        LogEntityType entityType,
        LogAction action,
        long organizationId,
        Guid ownerId,
        DateTime createdAt,
        List<OrganizationLogItem>? items,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists an <see cref="OrganizationLog"/> row for the given entity, unless
    /// <paramref name="items"/> is empty - in which case nothing actually changed and there's
    /// nothing worth recording. Returns whether a row was written.
    /// </summary>
    Task<bool> RecordIfChanged(
        long entityId,
        LogEntityType entityType,
        LogAction action,
        long organizationId,
        Guid ownerId,
        DateTime createdAt,
        List<OrganizationLogItem> items,
        CancellationToken cancellationToken);
}

public class IssueHistoryService(DatabaseContext context) : IIssueHistoryService
{
    public async Task Record(
        long entityId,
        LogEntityType entityType,
        LogAction action,
        long organizationId,
        Guid ownerId,
        DateTime createdAt,
        List<OrganizationLogItem>? items,
        CancellationToken cancellationToken)
    {
        context.Add(new OrganizationLog
        {
            CreatedAt = createdAt,
            EntityId = entityId,
            EntityType = entityType,
            Action = action,
            OrganizationId = organizationId,
            OwnerId = ownerId,
            Items = items,
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RecordIfChanged(
        long entityId,
        LogEntityType entityType,
        LogAction action,
        long organizationId,
        Guid ownerId,
        DateTime createdAt,
        List<OrganizationLogItem> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return false;

        await Record(
            entityId,
            entityType,
            action,
            organizationId,
            ownerId,
            createdAt,
            items,
            cancellationToken);

        return true;
    }
}
