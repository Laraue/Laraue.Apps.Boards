using System.ComponentModel;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Laraue.Apps.Boards.McpHost.Tools;

/// <summary>
/// MCP tools for reading and moving issues. Every method resolves the caller's
/// <see cref="OrganizationAuthData"/> from the request's <see cref="System.Security.Claims.ClaimsPrincipal"/> -
/// populated identically whether the caller authenticated with a JWT or an API key (see
/// <c>ApiKeyAuthenticationHandler</c>) - then goes through the exact same <see cref="IAccessService"/>
/// checks and <see cref="ICoreIssuesService"/> calls the REST API uses. No new permission or
/// mutation logic lives here.
/// </summary>
[McpServerToolType]
public class IssueTools(
    DatabaseContext context,
    IAccessService accessService,
    ICoreIssuesService coreIssuesService,
    IHttpContextAccessor httpContextAccessor)
{
    private const int MaxResults = 50;
    private const int TitleSnippetLength = 120;

    [McpServerTool]
    [Description("Lists issues in the caller's organization, optionally filtered by space key, status name, or assignee display name. Returns at most 50 issues, most recently updated first.")]
    public Task<IReadOnlyList<IssueSummary>> ListIssues(
        [Description("Only issues in this space (e.g. 'BRD'). Omit to search every space.")] string? spaceKey,
        [Description("Only issues with this exact status name (e.g. 'In Progress'). Omit to include every status.")] string? statusName,
        [Description("Only issues assigned to a user whose display name contains this text. Omit to include every assignee.")] string? assigneeName,
        CancellationToken cancellationToken)
    {
        var authData = GetAuthData();

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

    [McpServerTool]
    [Description("Gets one issue's full content and comments by its key (e.g. 'BRD-42').")]
    public async Task<IssueDetail> GetIssue(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        CancellationToken cancellationToken)
    {
        var authData = GetAuthData();
        var key = new IssueKey(issueKey);

        var issueId = await GetIssueIdByIssueKey(authData.OrganizationId, key, cancellationToken);

        await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound($"Issue: {key} is not found or not accessible")
            .EnsureOrThrowForbidden(a => a.CanRead, $"Issue: {key} is not readable");

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
            .Select(c => new IssueCommentSummary(c.Owner!.DisplayName, c.Text, c.CreatedAt))
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

    [McpServerTool]
    [Description("Moves an issue to a different status by name, within the issue's own epic.")]
    public async Task MoveIssueStatus(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        [Description("The target status's name, e.g. 'Done'. Must already exist in the issue's epic.")] string statusName,
        CancellationToken cancellationToken)
    {
        var authData = GetAuthData();
        var key = new IssueKey(issueKey);

        var issueId = await GetIssueIdByIssueKey(authData.OrganizationId, key, cancellationToken);

        await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound($"Issue: {key} is not found or not accessible")
            .EnsureOrThrowForbidden(a => a.CanUpdateIssue, $"Issue: {key} cannot be updated");

        var epicId = await context.ActiveIssues()
            .Where(i => i.Id == issueId)
            .Select(i => i.Status!.EpicId)
            .SingleAsync(cancellationToken);

        var statusId = await context.ActiveStatuses()
            .Where(s => s.EpicId == epicId && s.Name == statusName)
            .Select(s => s.Id)
            .FirstOrThrowNotFoundEFAsync($"Status: {statusName} is not found in issue {key}'s epic", cancellationToken);

        var canMove = await accessService.CanMoveToStatus(authData, statusId, cancellationToken);
        if (!canMove)
            throw new NotFoundException($"Status: {statusName} is not found");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await coreIssuesService.UpdateIssuesStatus([issueId], statusId, authData.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private OrganizationAuthData GetAuthData()
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new UnauthorizedException("No HTTP context available to resolve the caller's identity.");

        return httpContext.User.GetOrganizationAuthData();
    }

    private Task<long> GetIssueIdByIssueKey(long organizationId, IssueKey issueKey, CancellationToken cancellationToken)
    {
        return context.IssueNumbers
            .Where(x => x.Number == issueKey.Number)
            .Where(x => x.Space!.Key == issueKey.SpaceKey)
            .Where(x => x.Space!.OrganizationId == organizationId)
            .Where(x => x.Issue!.DeletedAt == null)
            .Select(x => x.IssueId)
            .FirstOrThrowNotFoundEFAsync($"Issue: {issueKey} is not found in organization", cancellationToken);
    }

    private static string ContentSnippet(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var firstLine = content.Split('\n', 2)[0];

        return TextTruncation.Truncate(firstLine, TitleSnippetLength);
    }
}

public sealed record IssueSummary(string Key, string Title, string Status, string Assignee);

public sealed record IssueCommentSummary(string Author, string Text, DateTime CreatedAt);

public sealed record IssueDetail(
    string Key,
    string? Content,
    string Status,
    string Assignee,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<IssueCommentSummary> Comments);
