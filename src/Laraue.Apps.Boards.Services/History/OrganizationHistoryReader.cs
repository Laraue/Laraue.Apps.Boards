using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DataAccess.Extensions;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.History;

/// <summary>
/// Organization/issue history (<see cref="OrganizationLog"/>) mapped into readable changes, shared by the
/// hosts that show it (WebApiHost, McpHost). A plain read with <b>no permission checks</b>: callers resolve
/// and authorize first (issue <c>CanRead</c>, readable spaces) at host level and pass the result in.
/// Soft-deleted issues, comments and spaces are included on purpose - history must stay readable.
/// </summary>
public interface IOrganizationHistoryReader
{
    /// <summary>History of one issue and its comments, newest first.</summary>
    Task<ShortPaginatedResult<OrganizationHistoryItem>> GetIssueHistory(
        long issueId,
        string issueKey,
        PaginationData pagination,
        CancellationToken ct);

    /// <summary>Organization-wide history of issues and comments in <see cref="OrganizationHistoryQuery.ReadableSpaceIds"/>, newest first.</summary>
    Task<ShortPaginatedResult<OrganizationHistoryItem>> GetOrganizationHistory(
        OrganizationHistoryQuery query,
        CancellationToken ct);
}

public sealed record OrganizationHistoryQuery(
    long OrganizationId,
    IReadOnlyCollection<long> ReadableSpaceIds,
    Guid? OwnerId,
    DateTime? DateFrom,
    DateTime? DateTo,
    PaginationData Pagination);

public class OrganizationHistoryReader(DatabaseContext context) : IOrganizationHistoryReader
{
    public async Task<ShortPaginatedResult<OrganizationHistoryItem>> GetIssueHistory(
        long issueId,
        string issueKey,
        PaginationData pagination,
        CancellationToken ct)
    {
        var updatesData = await context
            .OrganizationLogs
            .Where(x =>
                (x.EntityType == LogEntityType.Comment && context.IssueComments.Any(y => y.Id == x.EntityId && y.IssueId == issueId))
                || (x.EntityId == issueId && x.EntityType == LogEntityType.Issue))
            .OrderByDescending(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.CreatedAt,
                x.EntityType,
                x.Action,
                x.Owner!.Color,
                x.Owner.DisplayName,
                x.Owner.Initials,
                x.ApiKey!.Name,
                Items = x.Items!
                    .OrderBy(i => i.Id)
                    .ToArray(),
            })
            .ShortPaginateEFAsync(pagination, ct);

        var changes = await MapHistoryChanges(
            updatesData.Data.ToDictionary(
                x => x.Id,
                x => x.Items),
            ct);

        var result = updatesData.MapTo(x => new OrganizationHistoryItem
        {
            CreatedAt = x.CreatedAt,
            Owner = new UserDetails
            {
                Color = x.Color,
                DisplayName = x.DisplayName,
                Initials = x.Initials,
            },
            ApiKeyName = x.Name,
            Changes = changes[x.Id],
            EntityType = x.EntityType,
            Action = x.Action,
            IssueKey = issueKey,
        });

        return result;
    }

    public async Task<ShortPaginatedResult<OrganizationHistoryItem>> GetOrganizationHistory(
        OrganizationHistoryQuery query,
        CancellationToken ct)
    {
        var readableSpaceIds = query.ReadableSpaceIds;

        var logs = context.OrganizationLogs
            .Where(x => x.OrganizationId == query.OrganizationId)
            .Where(x =>
                (x.EntityType == LogEntityType.Issue
                 && context.Issues.Any(i => i.Id == x.EntityId && readableSpaceIds.Contains(i.Status!.Epic!.SpaceId)))
                || (x.EntityType == LogEntityType.Comment
                    && context.IssueComments.Any(c => c.Id == x.EntityId && readableSpaceIds.Contains(c.Issue!.Status!.Epic!.SpaceId))));

        if (query.OwnerId is not null)
            logs = logs.Where(x => x.OwnerId == query.OwnerId);

        if (query.DateFrom is not null)
            logs = logs.Where(x => x.CreatedAt >= query.DateFrom);

        if (query.DateTo is not null)
            logs = logs.Where(x => x.CreatedAt <= query.DateTo);

        var updatesData = await logs
            .OrderByDescending(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.EntityId,
                x.CreatedAt,
                x.EntityType,
                x.Action,
                x.Owner!.Color,
                x.Owner.DisplayName,
                x.Owner.Initials,
                x.ApiKey!.Name,
                Items = x.Items!
                    .OrderBy(i => i.Id)
                    .ToArray(),
            })
            .ShortPaginateEFAsync(query.Pagination, ct);

        var changes = await MapHistoryChanges(
            updatesData.Data.ToDictionary(
                x => x.Id,
                x => x.Items),
            ct);

        var issueKeysByLogId = await MapIssueKeysByLogId(
            updatesData.Data.Select(x => (x.Id, x.EntityId, x.EntityType)).ToArray(),
            ct);

        var result = updatesData.MapTo(x => new OrganizationHistoryItem
        {
            CreatedAt = x.CreatedAt,
            Owner = new UserDetails
            {
                Color = x.Color,
                DisplayName = x.DisplayName,
                Initials = x.Initials,
            },
            ApiKeyName = x.Name,
            Changes = changes[x.Id],
            EntityType = x.EntityType,
            Action = x.Action,
            IssueKey = issueKeysByLogId.GetValueOrDefault(x.Id),
        });

        return result;
    }

    private async Task<Dictionary<long, string?>> MapIssueKeysByLogId(
        (long LogId, long? EntityId, LogEntityType EntityType)[] entries,
        CancellationToken ct)
    {
        var issueEntityIds = entries
            .Where(x => x.EntityType == LogEntityType.Issue && x.EntityId is not null)
            .Select(x => x.EntityId!.Value)
            .Distinct()
            .ToArray();

        var commentEntityIds = entries
            .Where(x => x.EntityType == LogEntityType.Comment && x.EntityId is not null)
            .Select(x => x.EntityId!.Value)
            .Distinct()
            .ToArray();

        var commentIssueIds = await context.IssueComments
            .Where(c => commentEntityIds.Contains(c.Id))
            .Select(c => new { c.Id, c.IssueId })
            .ToDictionaryAsyncEF(c => c.Id, c => c.IssueId, ct);

        var allIssueIds = issueEntityIds
            .Concat(commentIssueIds.Values)
            .Distinct()
            .ToArray();

        var issueKeysByIssueId = await context.IssueNumbers
            .Where(x => allIssueIds.Contains(x.IssueId))
            .Select(x => new { x.IssueId, x.Number, SpaceKey = x.Space!.Key })
            .ToDictionaryAsyncEF(x => x.IssueId, x => new IssueKey(x.SpaceKey, x.Number).ToString(), ct);

        return entries.ToDictionary(
            x => x.LogId,
            x =>
            {
                if (x.EntityId is null)
                    return null;

                var issueId = x.EntityType == LogEntityType.Issue
                    ? x.EntityId.Value
                    : commentIssueIds.GetValueOrDefault(x.EntityId.Value);

                return issueKeysByIssueId.GetValueOrDefault(issueId);
            });
    }

    private async Task<Dictionary<long, HistoryItemChange[]>> MapHistoryChanges(
        Dictionary<long, OrganizationLogItem[]> changes,
        CancellationToken cancellationToken)
    {
        var allChanges = changes
            .SelectMany(x => x.Value)
            .ToArray();

        var possibleStatusIds = allChanges
            .Where(x => x.PropertyType == PropertyType.Status)
            .SelectMany(x => new[] { x.OldValueId, x.NewValueId })
            .Distinct()
            .Where(x => long.TryParse(x, out _))
            .Select(long.Parse!);

        var possibleAssigneeIds = allChanges
            .Where(x => x.PropertyType == PropertyType.Assignee)
            .SelectMany(x => new[] { x.OldValueId, x.NewValueId })
            .Distinct()
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse!);

        var possibleAttributeIds = allChanges
            .Where(x => x.PropertyType == PropertyType.Attribute)
            .Select(x => x.ParentId)
            .Distinct()
            .Where(x => long.TryParse(x, out _))
            .Select(long.Parse!);

        var possibleEpicIds = allChanges
            .Where(x => x.PropertyType == PropertyType.Epic)
            .SelectMany(x => new[] { x.OldValueId, x.NewValueId })
            .Distinct()
            .Where(x => long.TryParse(x, out _))
            .Select(long.Parse!);

        var possibleSpacesIds = allChanges
            .Where(x => x.PropertyType == PropertyType.Space)
            .SelectMany(x => new[] { x.OldValueId, x.NewValueId })
            .Distinct()
            .Where(x => long.TryParse(x, out _))
            .Select(long.Parse!);

        var statusColors = await context.Statuses
            .Where(s => possibleStatusIds.Contains(s.Id))
            .ToDictionaryAsyncEF(s => s.Id.ToString(), s => s.Color, cancellationToken);

        var userColors = await context.Users
            .Where(s => possibleAssigneeIds.Contains(s.Id))
            .ToDictionaryAsyncEF(s => s.Id.ToString(), s => s.Color, cancellationToken);

        var attributes = (await context.Attributes
            .Where(s => possibleAttributeIds.Contains(s.Id))
            .Select(s => new { Id = s.Id.ToString(), s.AttributeType, s.Color })
            .ToArrayAsyncEF(cancellationToken))
            .ToDictionary(x => x.Id, x => new AttributeData(x.Color, x.AttributeType));

        var epicColors = await context.Epics
            .Where(s => possibleEpicIds.Contains(s.Id))
            .ToDictionaryAsyncEF(s => s.Id.ToString(), s => s.Color, cancellationToken);

        var spacesColors = await context.Spaces
            .Where(s => possibleSpacesIds.Contains(s.Id))
            .ToDictionaryAsyncEF(s => s.Id.ToString(), s => s.Color, cancellationToken);

        var result = changes
            .Select(x => new
            {
                x.Key,
                Changes = x.Value.Select(y => MapChange(
                    y,
                    statusColors,
                    userColors,
                    attributes,
                    epicColors,
                    spacesColors))
            })
            .ToDictionary(x => x.Key, x => x.Changes.ToArray());

        return result;
    }

    private record AttributeData(string Color, AttributeType Type);
    
    private static HistoryItemChange MapChange(
        OrganizationLogItem item,
        Dictionary<string, string> statusColors,
        Dictionary<string, string> userColors,
        Dictionary<string, AttributeData> attributes,
        Dictionary<string, string> epicColors,
        Dictionary<string, string> spacesColors)
    {
        return item.PropertyType switch
        {
            PropertyType.Content => new IssueHistoryContentChange
            {
                NewContent = item.NewDisplayValue,
                OldContent = item.OldDisplayValue,
            },
            PropertyType.Assignee => new IssueHistoryAssigneeChange
            {
                OldAssigneeDisplayName = item.OldDisplayValue,
                NewAssigneeDisplayName = item.NewDisplayValue,
                OldAssigneeColor = item.OldValueId is not null ? userColors[item.OldValueId] : null,
                NewAssigneeColor = item.NewValueId is not null ? userColors[item.NewValueId] : null,
            },
            PropertyType.Status => new IssueHistoryStatusChange
            {
                NewStatusName = item.NewDisplayValue,
                NewStatusColor = item.NewValueId is not null ? statusColors[item.NewValueId] : null,
                OldStatusName = item.OldDisplayValue,
                OldStatusColor = item.OldValueId is not null ? statusColors[item.OldValueId] : null,
            },
            PropertyType.Attribute => new IssueHistoryPropertyChange
            {
                PropertyName = item.PropertyName ?? string.Empty,
                AttributeType = item.ParentId is not null ? attributes[item.ParentId].Type : default,
                NewValueName = item.NewDisplayValue,
                NewValueColor = item.ParentId is not null ? attributes[item.ParentId].Color : null,
                OldValueName = item.OldDisplayValue,
                OldValueColor = item.ParentId is not null ? attributes[item.ParentId].Color : null,
            },
            PropertyType.Attachment => new IssueHistoryAttachmentChange
            {
                PreviewFileId = Guid.TryParse(item.NewValueId, out var addedFileId)
                    ? addedFileId
                    : Guid.TryParse(item.OldValueId, out var deletedFile)
                        ? deletedFile
                        : null,
                FileName = item.NewDisplayValue ?? item.OldDisplayValue,
                Action = item.NewValueId is not null || item.NewDisplayValue is not null
                    ? AttachmentAction.Created
                    : AttachmentAction.Deleted,
            },
            PropertyType.Epic => new IssueHistoryEpicChange
            {
                NewEpicName = item.NewDisplayValue,
                NewEpicColor = item.NewValueId is not null ? epicColors[item.NewValueId] : null,
                OldEpicName = item.OldDisplayValue,
                OldEpicColor = item.OldValueId is not null ? epicColors[item.OldValueId] : null,
            },
            PropertyType.Space => new IssueHistorySpaceChange
            {
                NewSpaceName = item.NewDisplayValue,
                NewSpaceColor = item.NewValueId is not null ? spacesColors[item.NewValueId] : null,
                OldSpaceName = item.OldDisplayValue,
                OldSpaceColor = item.OldValueId is not null ? spacesColors[item.OldValueId] : null,
            },
            _ => throw new InvalidOperationException($"Change of type {item.PropertyType} is not supported yet")
        };
    }
}
