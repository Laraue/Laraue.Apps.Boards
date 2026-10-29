using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DataAccess.Extensions;
using LinqToDB.EntityFrameworkCore;

using Laraue.Apps.Boards.Services.Members;

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
        long organizationId,
        long issueId,
        string issueKey,
        PaginationData pagination,
        CancellationToken ct);

    /// <summary>Organization-wide history of issues and comments in <see cref="OrganizationHistoryQuery.ReadableSpaceIds"/>, newest first.</summary>
    Task<ShortPaginatedResult<OrganizationHistoryItem>> GetOrganizationHistory(
        OrganizationHistoryQuery query,
        CancellationToken ct);

    /// <summary>
    /// The API key each of <paramref name="commentIds"/> was written through (e.g. an agent's), from the comment's
    /// create entry - what the history shows as "via API key ...", with the agent when the key came from agent
    /// sign-in. One query for a page of comments; a comment written in the app has no entry in the result.
    /// </summary>
    Task<Dictionary<long, CommentApiKey>> GetCommentApiKeys(
        IReadOnlyCollection<long> commentIds,
        CancellationToken ct);
}

public sealed record OrganizationHistoryQuery(
    long OrganizationId,
    IReadOnlyCollection<long> ReadableSpaceIds,
    Guid? OwnerId,
    DateTime? DateFrom,
    DateTime? DateTo,
    PaginationData Pagination);

public class OrganizationHistoryReader(
    DatabaseContext context,
    IMemberProfileReader memberProfileReader)
    : IOrganizationHistoryReader
{
    public async Task<ShortPaginatedResult<OrganizationHistoryItem>> GetIssueHistory(
        long organizationId,
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
            .Select(x => new HistoryLogRow
            {
                Id = x.Id,
                EntityId = x.EntityId,
                CreatedAt = x.CreatedAt,
                EntityType = x.EntityType,
                Action = x.Action,
                OwnerId = x.OwnerId,
                ApiKeyId = x.ApiKeyId,
                ApiKeyName = x.ApiKey!.Name,
            })
            .ShortPaginateEFAsync(pagination, ct);

        var changes = await MapHistoryChanges(
            organizationId,
            await GetLogItems(updatesData.Data, ct),
            ct);

        var agents = await GetAgents(
            updatesData.Data
                .Select(x => x.ApiKeyId),
            ct);

        var issueTitle = await context.Issues
            .Where(x => x.Id == issueId)
            .Select(x => x.Title)
            .FirstAsyncEF(ct);

        var result = updatesData.MapTo(x => new OrganizationHistoryItem
        {
            CreatedAt = x.CreatedAt,
            Owner = new UserDetails { UserId = x.OwnerId },
            ApiKeyName = x.ApiKeyName,
            AuthorKind = GetAgent(agents, x.ApiKeyId) is null ? AuthorKind.User : AuthorKind.Agent,
            Agent = GetAgent(agents, x.ApiKeyId),
            Changes = changes[x.Id],
            EntityType = x.EntityType,
            Action = x.Action,
            IssueKey = issueKey,
            IssueTitle = issueTitle,
        });

        await memberProfileReader.EnrichUsers(
            organizationId,
            result.Data
                .Select(x => x.Owner),
            ct);

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
            .Select(x => new HistoryLogRow
            {
                Id = x.Id,
                EntityId = x.EntityId,
                CreatedAt = x.CreatedAt,
                EntityType = x.EntityType,
                Action = x.Action,
                OwnerId = x.OwnerId,
                ApiKeyId = x.ApiKeyId,
                ApiKeyName = x.ApiKey!.Name,
            })
            .ShortPaginateEFAsync(query.Pagination, ct);

        var changes = await MapHistoryChanges(
            query.OrganizationId,
            await GetLogItems(updatesData.Data, ct),
            ct);

        var agents = await GetAgents(
            updatesData.Data
                .Select(x => x.ApiKeyId),
            ct);

        var issueInfoByLogId = await MapIssueInfoByLogId(
            updatesData.Data.Select(x => (x.Id, x.EntityId, x.EntityType)).ToArray(),
            ct);

        var result = updatesData.MapTo(x => new OrganizationHistoryItem
        {
            CreatedAt = x.CreatedAt,
            Owner = new UserDetails { UserId = x.OwnerId },
            ApiKeyName = x.ApiKeyName,
            AuthorKind = GetAgent(agents, x.ApiKeyId) is null ? AuthorKind.User : AuthorKind.Agent,
            Agent = GetAgent(agents, x.ApiKeyId),
            Changes = changes[x.Id],
            EntityType = x.EntityType,
            Action = x.Action,
            IssueKey = issueInfoByLogId.GetValueOrDefault(x.Id)?.Key,
            IssueTitle = issueInfoByLogId.GetValueOrDefault(x.Id)?.Title,
        });

        await memberProfileReader.EnrichUsers(
            query.OrganizationId,
            result.Data
                .Select(x => x.Owner),
            ct);

        return result;
    }

    public async Task<Dictionary<long, CommentApiKey>> GetCommentApiKeys(
        IReadOnlyCollection<long> commentIds,
        CancellationToken ct)
    {
        var keys = await context.OrganizationLogs
            .Where(l => l.EntityType == LogEntityType.Comment
                && l.Action == LogAction.Create
                && l.ApiKeyId != null
                && commentIds.Contains(l.EntityId!.Value))
            .Select(l => new
            {
                CommentId = l.EntityId!.Value,
                l.ApiKeyId,
                l.ApiKey!.Name,
            })
            .ToArrayAsyncEF(ct);

        var agents = await GetAgents(
            keys
                .Select(x => x.ApiKeyId),
            ct);

        return keys.ToDictionary(
            x => x.CommentId,
            x => new CommentApiKey { Name = x.Name, Agent = GetAgent(agents, x.ApiKeyId) });
    }

    /// <summary>
    /// The page's history entries without their changes - loaded by <see cref="GetLogItems"/> - and agents - by
    /// <see cref="GetAgents"/>, so each is one flat query.
    /// </summary>
    private sealed record HistoryLogRow
    {
        public required long Id { get; init; }
        public required long? EntityId { get; init; }
        public required DateTime CreatedAt { get; init; }
        public required LogEntityType EntityType { get; init; }
        public required LogAction Action { get; init; }
        public required Guid OwnerId { get; init; }
        public required Guid? ApiKeyId { get; init; }
        public required string? ApiKeyName { get; init; }
    }

    /// <summary>
    /// The changes of each of <paramref name="logs"/>, in the order they were recorded; an entry with none gets an
    /// empty array.
    /// </summary>
    private async Task<Dictionary<long, OrganizationLogItem[]>> GetLogItems(
        IList<HistoryLogRow> logs,
        CancellationToken ct)
    {
        var logIds = logs
            .Select(x => x.Id)
            .ToArray();

        var items = await context.OrganizationLogItems
            .Where(i => logIds.Contains(i.OrganizationLogId))
            .OrderBy(i => i.Id)
            .ToArrayAsyncEF(ct);

        var itemsByLogId = items
            .GroupBy(i => i.OrganizationLogId)
            .ToDictionary(g => g.Key, g => g.ToArray());

        return logIds.ToDictionary(
            id => id,
            id => itemsByLogId.GetValueOrDefault(id) ?? []);
    }

    /// <summary>
    /// The agent behind each of <paramref name="apiKeyIds"/> issued by agent sign-in; other keys aren't in the result.
    /// </summary>
    private async Task<Dictionary<Guid, AgentDetails>> GetAgents(
        IEnumerable<Guid?> apiKeyIds,
        CancellationToken ct)
    {
        var keyIds = apiKeyIds
            .OfType<Guid>()
            .Distinct()
            .ToArray();

        if (keyIds.Length == 0)
            return [];

        var agents = await context.AgentSignInRequests
            .Where(r => keyIds.Contains(r.ApiKeyId!.Value))
            .Select(r => new
            {
                ApiKeyId = r.ApiKeyId!.Value,
                Agent = new AgentDetails { ClientName = r.ClientName, MachineName = r.MachineName },
            })
            .ToArrayAsyncEF(ct);

        // One sign-in issues one key; DistinctBy only guards the dictionary.
        return agents
            .DistinctBy(x => x.ApiKeyId)
            .ToDictionary(x => x.ApiKeyId, x => x.Agent);
    }

    private static AgentDetails? GetAgent(Dictionary<Guid, AgentDetails> agents, Guid? apiKeyId)
    {
        return apiKeyId is { } id ? agents.GetValueOrDefault(id) : null;
    }

    private sealed record IssueInfo(string Key, string Title);

    private async Task<Dictionary<long, IssueInfo?>> MapIssueInfoByLogId(
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

        var issueTitlesByIssueId = await context.Issues
            .Where(x => allIssueIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Title })
            .ToDictionaryAsyncEF(x => x.Id, x => x.Title, ct);

        return entries.ToDictionary(
            x => x.LogId,
            x =>
            {
                if (x.EntityId is null)
                    return null;

                var issueId = x.EntityType == LogEntityType.Issue
                    ? x.EntityId.Value
                    : commentIssueIds.GetValueOrDefault(x.EntityId.Value);

                var key = issueKeysByIssueId.GetValueOrDefault(issueId);

                return key is null ? null : new IssueInfo(key, issueTitlesByIssueId[issueId]);
            });
    }

    private async Task<Dictionary<long, HistoryItemChange[]>> MapHistoryChanges(
        long organizationId,
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

        var statuses = await context.Statuses
            .Where(s => possibleStatusIds.Contains(s.Id))
            .ToDictionaryAsyncEF(s => s.Id.ToString(), s => new StatusData(s.Color, s.Category), cancellationToken);

        var assignees = possibleAssigneeIds.Select(x => new UserDetails { UserId = x }).ToArray();
        await memberProfileReader.EnrichUsers(
            organizationId,
            assignees,
            cancellationToken);
        var userColors = assignees.ToDictionary(x => x.UserId.ToString(), x => x.Color);

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
                    statuses,
                    userColors,
                    attributes,
                    epicColors,
                    spacesColors))
            })
            .ToDictionary(x => x.Key, x => x.Changes.ToArray());

        return result;
    }

    private record AttributeData(string Color, AttributeType Type);
    private record StatusData(string Color, StatusCategory Category);
    
    private static HistoryItemChange MapChange(
        OrganizationLogItem item,
        Dictionary<string, StatusData> statuses,
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
            PropertyType.Title => new IssueHistoryTitleChange
            {
                NewTitle = item.NewDisplayValue,
                OldTitle = item.OldDisplayValue,
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
                NewStatusColor = item.NewValueId is not null ? statuses[item.NewValueId].Color : null,
                NewStatusCategory = item.NewValueId is not null ? statuses[item.NewValueId].Category : null,
                OldStatusName = item.OldDisplayValue,
                OldStatusColor = item.OldValueId is not null ? statuses[item.OldValueId].Color : null,
                OldStatusCategory = item.OldValueId is not null ? statuses[item.OldValueId].Category : null,
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
            PropertyType.AgentRun => new IssueHistoryAgentRunChange
            {
                RunId = long.Parse(item.ParentId!),
                OldState = item.OldValueId is not null ? (AgentRunState)int.Parse(item.OldValueId) : null,
                NewState = (AgentRunState)int.Parse(item.NewValueId!),
            },
            PropertyType.IssueLink => new IssueHistoryLinkChange
            {
                Relation = Enum.Parse<IssueLinkRelation>(item.PropertyName!),
                IssueKey = item.NewDisplayValue ?? item.OldDisplayValue,
                Action = item.NewValueId is not null
                    ? IssueLinkAction.Added
                    : IssueLinkAction.Removed,
            },
            _ => throw new InvalidOperationException($"Change of type {item.PropertyType} is not supported yet")
        };
    }
}
