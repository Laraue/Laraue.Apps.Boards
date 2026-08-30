using System.Text.Json.Serialization;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DataAccess.Extensions;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.WebApiServices;

public record GetOrganizationHistoryRequest : IPaginatedRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public Guid? OwnerId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public required PaginationData Pagination { get; set; }
}

public record GetIssueHistoryRequest : IPaginatedRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public string IssueKey { get; set; } = string.Empty;
    public required PaginationData Pagination { get; set; }
}

public record OrganizationHistoryItem
{
    public required DateTime CreatedAt { get; set; }
    public required UserDetails Owner { get; set; }
    public required HistoryItemChange[] Changes { get; set; }
    public required LogEntityType EntityType { get; set; }
    public required LogAction Action { get; set; }
    public required string? IssueKey { get; set; }
}

[JsonDerivedType(typeof(IssueHistoryContentChange), "content")]
[JsonDerivedType(typeof(IssueHistoryAssigneeChange), "assignee")]
[JsonDerivedType(typeof(IssueHistoryStatusChange), "status")]
[JsonDerivedType(typeof(IssueHistoryPropertyChange), "property")]
[JsonDerivedType(typeof(IssueHistoryAttachmentChange), "attachment")]
[JsonDerivedType(typeof(IssueHistoryEpicChange), "epic")]
[JsonDerivedType(typeof(IssueHistorySpaceChange), "space")]
public abstract record HistoryItemChange
{
}

public record IssueHistoryContentChange : HistoryItemChange
{
    public required string? OldContent { get; set; }
    public required string? NewContent { get; set; }
}

public record IssueHistoryAssigneeChange : HistoryItemChange
{
    public required string? OldAssigneeDisplayName { get; set; }
    public required string? OldAssigneeColor { get; set; }
    public required string? NewAssigneeDisplayName { get; set; }
    public required string? NewAssigneeColor { get; set; }
}

public record IssueHistoryStatusChange : HistoryItemChange
{
    public required string? OldStatusName { get; set; }
    public required string? OldStatusColor { get; set; }
    public required string? NewStatusName { get; set; }
    public required string? NewStatusColor { get; set; }
}

public record IssueHistoryPropertyChange : HistoryItemChange
{
    public required string PropertyName { get; set; }
    public required AttributeType AttributeType { get; set; }
    public required string? OldValueName { get; set; }
    public required string? OldValueColor { get; set; }
    public required string? NewValueName { get; set; }
    public required string? NewValueColor { get; set; }
}

public record IssueHistoryAttachmentChange : HistoryItemChange
{
    public required string? FileName { get; set; }
    public required Guid? PreviewFileId { get; set; }
    public required AttachmentAction Action { get; set; }
}

public enum AttachmentAction
{
    Created,
    Deleted,
}

public record IssueHistoryEpicChange : HistoryItemChange
{
    public required string? OldEpicName { get; set; }
    public required string? OldEpicColor { get; set; }
    public required string? NewEpicName { get; set; }
    public required string? NewEpicColor { get; set; }
}

public record IssueHistorySpaceChange : HistoryItemChange
{
    public required string? OldSpaceName { get; set; }
    public required string? OldSpaceColor { get; set; }
    public required string? NewSpaceName { get; set; }
    public required string? NewSpaceColor { get; set; }
}

public interface IOrganizationHistoryService
{
    Task<ShortPaginatedResult<OrganizationHistoryItem>> GetOrganizationHistory(
        GetOrganizationHistoryRequest request,
        CancellationToken ct);

    Task<ShortPaginatedResult<OrganizationHistoryItem>> GetIssueHistory(
        GetIssueHistoryRequest request,
        CancellationToken ct);
}

public class OrganizationHistoryService(
    DatabaseContext context,
    IAccessService accessService)
    : IOrganizationHistoryService
{
    public async Task<ShortPaginatedResult<OrganizationHistoryItem>> GetIssueHistory(
        GetIssueHistoryRequest request,
        CancellationToken ct)
    {
        var issueId = await GetIssueIdByIssueKey(
            request.AuthData.OrganizationId,
            new IssueKey(request.IssueKey),
            ct);

        await accessService.GetAccessLevelsByIssueId(request.AuthData, issueId, ct)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", request.IssueKey))
            .EnsureOrThrowNotFound(a => a.CanRead, string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", request.IssueKey));

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
                Items = x.Items!
                    .OrderBy(i => i.Id)
                    .ToArray(),
            })
            .ShortPaginateEFAsync(request.Pagination, ct);

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
            Changes = changes[x.Id],
            EntityType = x.EntityType,
            Action = x.Action,
            IssueKey = request.IssueKey,
        });

        return result;
    }

    private Task<long> GetIssueIdByIssueKey(
        long organizationId,
        IssueKey issueKey,
        CancellationToken cancellationToken)
    {
        return context.IssueNumbers
            .Where(x => x.Number == issueKey.Number)
            .Where(x => x.Space!.Key == issueKey.SpaceKey)
            .Where(x => x.Space!.OrganizationId == organizationId)
            .Select(x => x.IssueId)
            .FirstOrThrowNotFoundEFAsync($"Issue: {issueKey} is not found in organization", cancellationToken);
    }

    public async Task<ShortPaginatedResult<OrganizationHistoryItem>> GetOrganizationHistory(
        GetOrganizationHistoryRequest request,
        CancellationToken ct)
    {
        var readableSpaceIds = await accessService.GetAvailableSpaces(
            request.AuthData,
            query => query.Select(s => s.Id).ToArrayAsyncEF(ct),
            ct);

        var query = context.OrganizationLogs
            .Where(x => x.OrganizationId == request.AuthData.OrganizationId)
            .Where(x =>
                (x.EntityType == LogEntityType.Issue
                 && context.Issues.Any(i => i.Id == x.EntityId && readableSpaceIds.Contains(i.Status!.Epic!.SpaceId)))
                || (x.EntityType == LogEntityType.Comment
                    && context.IssueComments.Any(c => c.Id == x.EntityId && readableSpaceIds.Contains(c.Issue!.Status!.Epic!.SpaceId))));

        if (request.OwnerId is not null)
            query = query.Where(x => x.OwnerId == request.OwnerId);

        if (request.DateFrom is not null)
            query = query.Where(x => x.CreatedAt >= request.DateFrom);

        if (request.DateTo is not null)
            query = query.Where(x => x.CreatedAt <= request.DateTo);

        var updatesData = await query
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
                Items = x.Items!
                    .OrderBy(i => i.Id)
                    .ToArray(),
            })
            .ShortPaginateEFAsync(request.Pagination, ct);

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
