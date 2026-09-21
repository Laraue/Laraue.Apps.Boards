using System.ComponentModel;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.McpHost.Services;
using Laraue.Core.Exceptions.Web;
using ModelContextProtocol.Server;

namespace Laraue.Apps.Boards.McpHost.Tools;

/// <summary>
/// MCP tools for reading and moving issues - a thin adapter, same shape as a controller: resolve
/// the caller's identity, call into <see cref="IIssueMcpService"/>, return the result. All actual
/// logic lives there.
/// </summary>
[McpServerToolType]
public class IssueTools(IIssueMcpService issueMcpService, IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool]
    [Description("Lists issues in the caller's organization, optionally filtered by space key, status name, or assignee display name. Returns at most 50 issues, most recently updated first.")]
    public Task<IReadOnlyList<IssueSummary>> ListIssues(
        [Description("Only issues in this space (e.g. 'BRD'). Omit to search every space.")] string? spaceKey,
        [Description("Only issues with this exact status name (e.g. 'In Progress'). Omit to include every status.")] string? statusName,
        [Description("Only issues assigned to a user whose display name contains this text. Omit to include every assignee.")] string? assigneeName,
        CancellationToken cancellationToken)
    {
        return issueMcpService.ListIssues(GetAuthData(), spaceKey, statusName, assigneeName, cancellationToken);
    }

    [McpServerTool]
    [Description("Gets one issue's full content and comments by its key (e.g. 'BRD-42').")]
    public Task<IssueDetail> GetIssue(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        CancellationToken cancellationToken)
    {
        return issueMcpService.GetIssue(GetAuthData(), issueKey, cancellationToken);
    }

    [McpServerTool]
    [Description("Moves an issue to a different status by name, within the issue's own epic.")]
    public Task MoveIssueStatus(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        [Description("The target status's name, e.g. 'Done'. Must already exist in the issue's epic.")] string statusName,
        CancellationToken cancellationToken)
    {
        return issueMcpService.MoveIssueStatus(GetAuthData(), issueKey, statusName, cancellationToken);
    }

    [McpServerTool]
    [Description("Creates a new issue in a space and returns its key. If a status name isn't given, the space's default status is used.")]
    public Task<string> CreateIssue(
        [Description("The space to create the issue in, e.g. 'BRD'.")] string spaceKey,
        [Description("The issue's text content.")] string content,
        [Description("The status to create the issue in, e.g. 'Backlog'. Omit to use the space's default status.")] string? statusName,
        CancellationToken cancellationToken)
    {
        return issueMcpService.CreateIssue(GetAuthData(), spaceKey, content, statusName, cancellationToken);
    }

    [McpServerTool]
    [Description("Replaces an issue's text content.")]
    public Task EditIssue(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        [Description("The issue's new text content, replacing what's there now.")] string content,
        CancellationToken cancellationToken)
    {
        return issueMcpService.EditIssue(GetAuthData(), issueKey, content, cancellationToken);
    }

    [McpServerTool]
    [Description("Adds a comment to an issue and returns the new comment's id.")]
    public Task<long> AddComment(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        [Description("The comment's text.")] string text,
        CancellationToken cancellationToken)
    {
        return issueMcpService.AddComment(GetAuthData(), issueKey, text, cancellationToken);
    }

    [McpServerTool]
    [Description("Edits a comment's text. Only the comment's own author can edit it - get_issue returns each comment's id.")]
    public Task EditComment(
        [Description("The id of the comment to edit, from get_issue's comment list.")] long commentId,
        [Description("The comment's new text, replacing what's there now.")] string text,
        CancellationToken cancellationToken)
    {
        return issueMcpService.EditComment(GetAuthData(), commentId, text, cancellationToken);
    }

    private OrganizationAuthData GetAuthData()
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new UnauthorizedException("No HTTP context available to resolve the caller's identity.");

        return httpContext.User.GetOrganizationAuthData();
    }
}
