using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.McpHost.Resources;
using Laraue.Apps.Boards.Services;
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
    Task<IReadOnlyList<IssueSummary>> ListIssues(
        OrganizationAuthData authData,
        string? spaceKey,
        string? statusName,
        string? assigneeName,
        CancellationToken cancellationToken);

    Task<IssueDetail> GetIssue(
        OrganizationAuthData authData,
        string issueKey,
        CancellationToken cancellationToken);

    Task MoveIssueStatus(
        OrganizationAuthData authData,
        string issueKey,
        string statusName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates an issue in <paramref name="spaceKey"/>. When <paramref name="statusName"/> is
    /// omitted, the space's default epic's first status (by sort order) is used - the same
    /// "somewhere for a new card to land" concept every space/epic already has for its own
    /// default. Returns the new issue's key.
    /// </summary>
    Task<string> CreateIssue(
        OrganizationAuthData authData,
        string spaceKey,
        string content,
        string? statusName,
        CancellationToken cancellationToken);

    Task EditIssue(
        OrganizationAuthData authData,
        string issueKey,
        string content,
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

public sealed record IssueCommentSummary(long Id, string Author, string Text, DateTime CreatedAt);

public sealed record IssueDetail(
    string Key,
    string? Content,
    string Status,
    string Assignee,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<IssueCommentSummary> Comments);

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

    public Task<IReadOnlyList<IssueSummary>> ListIssues(
        OrganizationAuthData authData,
        string? spaceKey,
        string? statusName,
        string? assigneeName,
        CancellationToken cancellationToken)
    {
        return accessService.GetAvailableIssues<IReadOnlyList<IssueSummary>>(authData, async issues =>
        {
            var query = issues;

            if (!string.IsNullOrWhiteSpace(spaceKey))
                query = query.Where(i => i.Status!.Epic!.Space!.Key == spaceKey);

            if (!string.IsNullOrWhiteSpace(statusName))
                query = query.Where(i => i.Status!.Name == statusName);

            if (!string.IsNullOrWhiteSpace(assigneeName))
                query = query.Where(i => i.Assignee!.DisplayName.Contains(assigneeName));

            var rows = await query
                .OrderByDescending(i => i.UpdatedAt)
                .Take(MaxResults)
                .Select(i => new
                {
                    SpaceKey = i.IssueNumber!.Space!.Key,
                    i.IssueNumber.Number,
                    i.Content,
                    Status = i.Status!.Name,
                    Assignee = i.Assignee!.DisplayName,
                })
                .ToListAsyncEF(cancellationToken);

            return rows
                .Select(x => new IssueSummary(
                    new IssueKey(x.SpaceKey, x.Number).ToString(),
                    ContentSnippet(x.Content),
                    x.Status,
                    x.Assignee))
                .ToList();
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

    public async Task MoveIssueStatus(
        OrganizationAuthData authData,
        string issueKey,
        string statusName,
        CancellationToken cancellationToken)
    {
        var key = new IssueKey(issueKey);

        var issueId = await GetIssueIdByIssueKey(authData.OrganizationId, key, cancellationToken);

        await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", key))
            .EnsureOrThrowForbidden(a => a.CanUpdateIssue, string.Format(ErrorMessages.EntityActionForbidden, "Issue", key, "update"));

        var epicId = await context.ActiveIssues()
            .Where(i => i.Id == issueId)
            .Select(i => i.Status!.EpicId)
            .SingleAsync(cancellationToken);

        var statusId = await context.ActiveStatuses()
            .Where(s => s.EpicId == epicId && s.Name == statusName)
            .Select(s => s.Id)
            .FirstOrThrowNotFoundEFAsync(string.Format(ErrorMessages.StatusNotFoundInIssueEpic, statusName, key), cancellationToken);

        var canMove = await accessService.CanMoveToStatus(authData, statusId, cancellationToken);
        if (!canMove)
            throw new NotFoundException(string.Format(ErrorMessages.EntityNotFound, "Status", statusName));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await coreIssuesService.UpdateIssuesStatus([issueId], statusId, authData.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<string> CreateIssue(
        OrganizationAuthData authData,
        string spaceKey,
        string content,
        string? statusName,
        CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(authData.OrganizationId, spaceKey, cancellationToken);

        await accessService.GetAccessLevelsBySpaceId(authData, spaceId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Space", spaceKey))
            .EnsureOrThrowForbidden(a => a.CanCreateIssue, string.Format(ErrorMessages.EntityActionForbidden, "Space", spaceKey, "issue creation"));

        var statusId = string.IsNullOrWhiteSpace(statusName)
            ? await context.ActiveEpics()
                .Where(e => e.SpaceId == spaceId && e.IsDefault)
                .SelectMany(e => e.Statuses!.Where(s => s.DeletedAt == null))
                .OrderBy(s => s.SortOrder)
                .Select(s => s.Id)
                .FirstOrThrowNotFoundEFAsync(string.Format(ErrorMessages.SpaceHasNoDefaultStatus, spaceKey), cancellationToken)
            : await context.ActiveStatuses()
                .Where(s => s.Epic!.SpaceId == spaceId && s.Name == statusName)
                .Select(s => s.Id)
                .FirstOrThrowNotFoundEFAsync(string.Format(ErrorMessages.StatusNotFoundInSpace, statusName, spaceKey), cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var issueId = await coreIssuesService.Create(
            authData.UserId,
            new IssueCreateRequest(statusId, dateTimeProvider.UtcNow).SetContent(content),
            cancellationToken);

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
        CancellationToken cancellationToken)
    {
        var key = new IssueKey(issueKey);

        var issueId = await GetIssueIdByIssueKey(authData.OrganizationId, key, cancellationToken);

        await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", key))
            .EnsureOrThrowForbidden(a => a.CanUpdateIssue, string.Format(ErrorMessages.EntityActionForbidden, "Issue", key, "update"));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await coreIssuesService.Update(issueId, authData.UserId, new IssueUpdateRequest().SetContent(content), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
