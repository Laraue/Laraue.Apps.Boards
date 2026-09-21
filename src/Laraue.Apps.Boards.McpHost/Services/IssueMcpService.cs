using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.McpHost.Resources;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.AttributeRequests;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.McpHost.Services;

/// <summary>
/// Backs <see cref="Laraue.Apps.Boards.McpHost.Tools.IssueTools"/> - the tool type is a thin MCP
/// adapter (attributes + parameter descriptions only), this is where the actual query/permission/
/// mutation logic lives. Goes through the exact same <see cref="IAccessService"/> checks and
/// <see cref="ICoreIssuesService"/> calls the REST API uses - no new permission or mutation logic.
/// </summary>
public interface IIssueMcpService
{
    Task<IssueListPage> ListIssues(
        OrganizationAuthData authData,
        string? spaceKey,
        string? statusName,
        string? assigneeName,
        int? page,
        int? count,
        CancellationToken cancellationToken);

    Task<IssueDetail> GetIssue(
        OrganizationAuthData authData,
        string issueKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves the issue to <paramref name="statusId"/> - any status the caller can move issues to
    /// (via <see cref="IAccessService.CanMoveToStatus"/>), not necessarily one in the issue's
    /// current epic; moving to a different epic's status is the REST API's own behavior too,
    /// nothing MCP-specific. Call <see cref="ListStatuses"/> first to find a valid id.
    /// </summary>
    Task UpdateIssueStatus(
        OrganizationAuthData authData,
        string issueKey,
        long statusId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates an issue in <paramref name="spaceKey"/>. <paramref name="statusId"/> must belong
    /// to that space - call <see cref="ListStatuses"/> first to find one.
    /// <paramref name="attributes"/> maps attribute name to a plain-text value (see
    /// <see cref="ListAttributes"/> for what's available and its expected format per type) -
    /// omit or pass null/empty to leave every attribute unset. Returns the new issue's key.
    /// </summary>
    Task<string> CreateIssue(
        OrganizationAuthData authData,
        string spaceKey,
        string content,
        long statusId,
        IReadOnlyDictionary<string, string>? attributes,
        CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="attributes"/> maps attribute name to a plain-text value, same as
    /// <see cref="CreateIssue"/> - omitting it (null/empty) leaves every attribute untouched
    /// rather than clearing them.
    /// </summary>
    Task EditIssue(
        OrganizationAuthData authData,
        string issueKey,
        string content,
        IReadOnlyDictionary<string, string>? attributes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the statuses available in a space, grouped by epic - the ids a caller can pass to
    /// <see cref="CreateIssue"/>/<see cref="UpdateIssueStatus"/>.
    /// </summary>
    Task<IReadOnlyList<EpicStatusSummary>> ListStatuses(
        OrganizationAuthData authData,
        string spaceKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists every custom attribute defined for the caller's organization (attributes are
    /// org-wide, not scoped to a space/epic) - the names/types a caller can pass to
    /// <see cref="CreateIssue"/>/<see cref="EditIssue"/>'s <c>attributes</c> map, and for
    /// list-typed attributes, the allowed values.
    /// </summary>
    Task<IReadOnlyList<AttributeSummary>> ListAttributes(
        OrganizationAuthData authData,
        CancellationToken cancellationToken);

    /// <summary>Returns the new comment's id.</summary>
    Task<long> AddComment(
        OrganizationAuthData authData,
        string issueKey,
        string text,
        CancellationToken cancellationToken);

    /// <summary>
    /// Throws <see cref="NotFoundException"/> if the comment (or its issue) doesn't exist or
    /// isn't readable, <see cref="ForbiddenException"/> if it's readable but this caller isn't
    /// its author - only the comment's own owner may edit it, same rule the REST API enforces
    /// (<c>IssuesService.UpdateIssueComment</c>), not gated by <c>CanUpdateIssue</c>.
    /// </summary>
    Task EditComment(
        OrganizationAuthData authData,
        long commentId,
        string text,
        CancellationToken cancellationToken);
}

public sealed record IssueSummary(string Key, string Title, string Status, string Assignee);

/// <summary>
/// One page of <see cref="IssueSummary"/> results - same page/perPage/hasNextPage shape the REST
/// API's own paginated endpoints use (<c>ShortPaginatedResult{T}</c>), so callers page through
/// results the same way rather than being limited to a single fixed-size batch.
/// </summary>
public sealed record IssueListPage(IReadOnlyList<IssueSummary> Issues, long Page, bool HasNextPage);

public sealed record IssueCommentSummary(long Id, string Author, string Text, DateTime CreatedAt);

public sealed record IssueDetail(
    string Key,
    string? Content,
    string Status,
    string Assignee,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<IssueCommentSummary> Comments);

public sealed record StatusSummary(long Id, string Name);

public sealed record EpicStatusSummary(string EpicName, IReadOnlyList<StatusSummary> Statuses);

/// <summary>
/// <see cref="Type"/> is <see cref="AttributeType"/>'s name (e.g. "Text", "Integer", "Date") -
/// the expected format for the plain-text value <see cref="IIssueMcpService.CreateIssue"/>/
/// <see cref="IIssueMcpService.EditIssue"/> take per attribute. <see cref="ListValues"/> is only
/// populated for <see cref="AttributeType.List"/> - null otherwise.
/// </summary>
public sealed record AttributeSummary(string Name, string Type, IReadOnlyList<string>? ListValues);

public class IssueMcpService(
    DatabaseContext context,
    IAccessService accessService,
    ICoreIssuesService coreIssuesService,
    ICoreSpacesService coreSpacesService,
    IDateTimeProvider dateTimeProvider)
    : IIssueMcpService
{
    private const int MaxResults = 50;
    private const int TitleSnippetLength = 120;

    public Task<IssueListPage> ListIssues(
        OrganizationAuthData authData,
        string? spaceKey,
        string? statusName,
        string? assigneeName,
        int? page,
        int? count,
        CancellationToken cancellationToken)
    {
        var perPage = Math.Clamp(count ?? MaxResults, 1, MaxResults);
        var pagination = new PaginationData { Page = page ?? 0, PerPage = perPage };

        return accessService.GetAvailableIssues<IssueListPage>(authData, async issues =>
        {
            var query = issues;

            if (!string.IsNullOrWhiteSpace(spaceKey))
                query = query.Where(i => i.Status!.Epic!.Space!.Key == spaceKey);

            if (!string.IsNullOrWhiteSpace(statusName))
                query = query.Where(i => i.Status!.Name == statusName);

            if (!string.IsNullOrWhiteSpace(assigneeName))
                query = query.Where(i => i.Assignee!.DisplayName.Contains(assigneeName));

            var result = await query
                .OrderByDescending(i => i.UpdatedAt)
                .Select(i => new
                {
                    SpaceKey = i.IssueNumber!.Space!.Key,
                    i.IssueNumber.Number,
                    i.Content,
                    Status = i.Status!.Name,
                    Assignee = i.Assignee!.DisplayName,
                })
                .ShortPaginateEFAsync(pagination, cancellationToken);

            var summaries = result.Data
                .Select(x => new IssueSummary(
                    new IssueKey(x.SpaceKey, x.Number).ToString(),
                    ContentSnippet(x.Content),
                    x.Status,
                    x.Assignee))
                .ToList();

            return new IssueListPage(summaries, result.Page, result.HasNextPage);
        }, cancellationToken);
    }

    public async Task<IssueDetail> GetIssue(
        OrganizationAuthData authData,
        string issueKey,
        CancellationToken cancellationToken)
    {
        var key = new IssueKey(issueKey);

        var issueId = await GetIssueIdByIssueKey(authData.OrganizationId, key, cancellationToken);

        await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", key))
            .EnsureOrThrowForbidden(a => a.CanRead, string.Format(ErrorMessages.EntityActionForbidden, "Issue", key, "read"));

        var issue = await context.ActiveIssues()
            .Where(i => i.Id == issueId)
            .Select(i => new
            {
                i.Content,
                StatusName = i.Status!.Name,
                AssigneeName = i.Assignee!.DisplayName,
                i.CreatedAt,
                i.UpdatedAt,
            })
            .SingleAsync(cancellationToken);

        var comments = await context.ActiveIssueComments()
            .Where(c => c.IssueId == issueId)
            .OrderBy(c => c.Id)
            .Select(c => new IssueCommentSummary(c.Id, c.Owner!.DisplayName, c.Text, c.CreatedAt))
            .ToListAsyncEF(cancellationToken);

        return new IssueDetail(
            key.ToString(),
            issue.Content,
            issue.StatusName,
            issue.AssigneeName,
            issue.CreatedAt,
            issue.UpdatedAt,
            comments);
    }

    public async Task UpdateIssueStatus(
        OrganizationAuthData authData,
        string issueKey,
        long statusId,
        CancellationToken cancellationToken)
    {
        var key = new IssueKey(issueKey);

        var issueId = await GetIssueIdByIssueKey(authData.OrganizationId, key, cancellationToken);

        await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", key))
            .EnsureOrThrowForbidden(a => a.CanUpdateIssue, string.Format(ErrorMessages.EntityActionForbidden, "Issue", key, "update"));

        var canMove = await accessService.CanMoveToStatus(authData, statusId, cancellationToken);
        if (!canMove)
            throw new NotFoundException(string.Format(ErrorMessages.EntityNotFound, "Status", statusId));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await coreIssuesService.UpdateIssuesStatus([issueId], statusId, authData.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<string> CreateIssue(
        OrganizationAuthData authData,
        string spaceKey,
        string content,
        long statusId,
        IReadOnlyDictionary<string, string>? attributes,
        CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(authData.OrganizationId, spaceKey, cancellationToken);

        await accessService.GetAccessLevelsBySpaceId(authData, spaceId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Space", spaceKey))
            .EnsureOrThrowForbidden(a => a.CanCreateIssue, string.Format(ErrorMessages.EntityActionForbidden, "Space", spaceKey, "issue creation"));

        // statusId must actually belong to spaceKey's space - otherwise the permission check
        // above (against spaceKey) and the issue's real destination (derived from statusId's own
        // epic/space) could silently disagree, letting a caller create an issue in a space they
        // never had create access to just by naming a status from it.
        var resolvedStatusId = await context.ActiveStatuses()
            .Where(s => s.Id == statusId && s.Epic!.SpaceId == spaceId)
            .Select(s => s.Id)
            .FirstOrThrowNotFoundEFAsync(string.Format(ErrorMessages.StatusNotFoundInSpace, statusId, spaceKey), cancellationToken);

        var attributeRequests = await ResolveAttributeRequests(authData.OrganizationId, attributes, cancellationToken);

        var issueCreate = new IssueCreateRequest(resolvedStatusId, dateTimeProvider.UtcNow).SetContent(content);
        if (attributeRequests.Count > 0)
            issueCreate = issueCreate.SetAttributes(attributeRequests);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var issueId = await coreIssuesService.Create(authData.UserId, issueCreate, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await context.ActiveIssues()
            .Where(i => i.Id == issueId)
            .Select(i => new IssueKey(i.IssueNumber!.Space!.Key, i.IssueNumber.Number).ToString())
            .FirstAsyncEF(cancellationToken);
    }

    public async Task EditIssue(
        OrganizationAuthData authData,
        string issueKey,
        string content,
        IReadOnlyDictionary<string, string>? attributes,
        CancellationToken cancellationToken)
    {
        var key = new IssueKey(issueKey);

        var issueId = await GetIssueIdByIssueKey(authData.OrganizationId, key, cancellationToken);

        await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", key))
            .EnsureOrThrowForbidden(a => a.CanUpdateIssue, string.Format(ErrorMessages.EntityActionForbidden, "Issue", key, "update"));

        var attributeRequests = await ResolveAttributeRequests(authData.OrganizationId, attributes, cancellationToken);

        var issueUpdate = new IssueUpdateRequest().SetContent(content);
        if (attributeRequests.Count > 0)
            issueUpdate = issueUpdate.SetAttributes(attributeRequests);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await coreIssuesService.Update(issueId, authData.UserId, issueUpdate, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EpicStatusSummary>> ListStatuses(
        OrganizationAuthData authData,
        string spaceKey,
        CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(authData.OrganizationId, spaceKey, cancellationToken);

        await accessService.GetAccessLevelsBySpaceId(authData, spaceId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Space", spaceKey))
            .EnsureOrThrowForbidden(a => a.CanRead, string.Format(ErrorMessages.EntityActionForbidden, "Space", spaceKey, "read"));

        return await context.ActiveEpics()
            .Where(e => e.SpaceId == spaceId)
            .OrderBy(e => e.Id)
            .Select(e => new EpicStatusSummary(
                e.Name,
                e.Statuses!
                    .Where(s => s.DeletedAt == null)
                    .OrderBy(s => s.SortOrder)
                    .Select(s => new StatusSummary(s.Id, s.Name))
                    .ToList()))
            .ToListAsyncEF(cancellationToken);
    }

    public async Task<IReadOnlyList<AttributeSummary>> ListAttributes(
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        return await context.Attributes
            .Where(a => a.OrganizationId == authData.OrganizationId)
            .OrderBy(a => a.Id)
            .Select(a => new AttributeSummary(
                a.Name,
                a.AttributeType.ToString(),
                a.AttributeType == AttributeType.List
                    ? a.AttributeListValues!.Select(v => v.Value).ToList()
                    : null))
            .ToListAsyncEF(cancellationToken);
    }

    private async Task<IReadOnlyList<SetIssueAttributeRequest>> ResolveAttributeRequests(
        long organizationId,
        IReadOnlyDictionary<string, string>? attributes,
        CancellationToken cancellationToken)
    {
        if (attributes is null || attributes.Count == 0)
            return [];

        var requests = new List<SetIssueAttributeRequest>();

        foreach (var (name, value) in attributes)
        {
            var attribute = await context.Attributes
                .Where(a => a.OrganizationId == organizationId && a.Name == name)
                .Select(a => new { a.Id, a.AttributeType })
                .FirstOrThrowNotFoundEFAsync(string.Format(ErrorMessages.EntityNotFound, "Attribute", name), cancellationToken);

            requests.Add(await BuildAttributeRequest(attribute.Id, attribute.AttributeType, name, value, cancellationToken));
        }

        return requests;
    }

    private async Task<SetIssueAttributeRequest> BuildAttributeRequest(
        long attributeId,
        AttributeType attributeType,
        string attributeName,
        string value,
        CancellationToken cancellationToken)
    {
        switch (attributeType)
        {
            case AttributeType.Text:
                if (value.Length > 255)
                    throw new BadRequestException(nameof(value), string.Format(ErrorMessages.AttributeValueTooLong, attributeName));
                return new SetIssueTextAttributeRequest { Id = attributeId, Value = value };

            case AttributeType.Integer:
                if (!long.TryParse(value, out var integerValue))
                    throw new BadRequestException(nameof(value), string.Format(ErrorMessages.AttributeValueInvalid, attributeName, "integer"));
                return new SetIssueIntegerAttributeRequest { Id = attributeId, Value = integerValue };

            case AttributeType.Decimal:
                if (!decimal.TryParse(value, out var decimalValue))
                    throw new BadRequestException(nameof(value), string.Format(ErrorMessages.AttributeValueInvalid, attributeName, "decimal"));
                return new SetIssueDecimalAttributeRequest { Id = attributeId, Value = decimalValue };

            case AttributeType.Date:
                if (!DateOnly.TryParse(value, out var dateValue))
                    throw new BadRequestException(nameof(value), string.Format(ErrorMessages.AttributeValueInvalid, attributeName, "date (e.g. 2026-01-01)"));
                return new SetIssueDateAttributeRequest { Id = attributeId, Value = dateValue };

            case AttributeType.DateTime:
                if (!DateTime.TryParse(value, out var dateTimeValue))
                    throw new BadRequestException(nameof(value), string.Format(ErrorMessages.AttributeValueInvalid, attributeName, "date-time (e.g. 2026-01-01 12:00)"));
                return new SetIssueDateTimeAttributeRequest { Id = attributeId, Value = dateTimeValue };

            case AttributeType.List:
                var listValueId = await context.AttributeListValues
                    .Where(v => v.AttributeId == attributeId && v.Value == value)
                    .Select(v => v.Id)
                    .FirstOrThrowNotFoundEFAsync(string.Format(ErrorMessages.AttributeListValueNotFound, attributeName, value), cancellationToken);
                return new SetIssueListAttributeRequest { Id = attributeId, ListValueId = listValueId };

            default:
                throw new ArgumentOutOfRangeException(nameof(attributeType), attributeType, null);
        }
    }

    public async Task<long> AddComment(
        OrganizationAuthData authData,
        string issueKey,
        string text,
        CancellationToken cancellationToken)
    {
        var key = new IssueKey(issueKey);

        var issueId = await GetIssueIdByIssueKey(authData.OrganizationId, key, cancellationToken);

        await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", key))
            .EnsureOrThrowForbidden(a => a.CanUpdateIssue, string.Format(ErrorMessages.EntityActionForbidden, "Issue", key, "update"));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var commentId = await coreIssuesService.AddComment(issueId, authData.UserId, text, [], cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return commentId;
    }

    public async Task EditComment(
        OrganizationAuthData authData,
        long commentId,
        string text,
        CancellationToken cancellationToken)
    {
        // Two distinct failure modes: the comment/its issue doesn't exist or isn't readable
        // (404), vs. it's readable but this caller isn't its author (403) - same rule the REST
        // API's UpdateIssueComment enforces for the second case (owner-only, not CanUpdateIssue).
        await accessService.GetAccessLevelsByCommentId(authData, commentId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Comment", commentId))
            .EnsureOrThrowForbidden(a => a.CanRead, string.Format(ErrorMessages.EntityActionForbidden, "Comment", commentId, "read"));

        var comment = await context.ActiveIssueComments()
            .Where(x => x.Id == commentId)
            .Select(x => new { x.Id, x.OwnerId })
            .SingleAsync(cancellationToken);

        if (comment.OwnerId != authData.UserId)
            throw new ForbiddenException(string.Format(ErrorMessages.EntityActionForbidden, "Comment", commentId, "edit"));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await coreIssuesService.UpdateComment(comment.Id, comment.OwnerId, text, [], [], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task<long> GetIssueIdByIssueKey(long organizationId, IssueKey issueKey, CancellationToken cancellationToken)
    {
        return context.IssueNumbers
            .Where(x => x.Number == issueKey.Number)
            .Where(x => x.Space!.Key == issueKey.SpaceKey)
            .Where(x => x.Space!.OrganizationId == organizationId)
            .Where(x => x.Issue!.DeletedAt == null)
            .Select(x => x.IssueId)
            .FirstOrThrowNotFoundEFAsync(string.Format(ErrorMessages.IssueNotFoundInOrganization, issueKey), cancellationToken);
    }

    private static string ContentSnippet(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var firstLine = content.Split('\n', 2)[0];

        return TextTruncation.Truncate(firstLine, TitleSnippetLength);
    }
}
