using System.ComponentModel;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.McpHost.Services;
using Laraue.Core.Exceptions.Web;
using ModelContextProtocol.Server;

namespace Laraue.Apps.Boards.McpHost.Tools;

/// <summary>
/// MCP tools for reading and managing issues - a thin adapter, same shape as a controller: resolve
/// the caller's identity, call into <see cref="IIssueMcpService"/>, return the result. All actual
/// logic lives there.
/// </summary>
[McpServerToolType]
public class IssueTools(IIssueMcpService issueMcpService, IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool]
    [Description("Lists issues in the caller's organization, optionally filtered by space key, status name, or assignee display name. Returns at most 50 issues per page (fewer if count is given), most recently updated first. Check the result's hasNextPage to know whether to request another page.")]
    public Task<IssueListPage> ListIssues(
        [Description("Only issues in this space (e.g. 'BRD'). Omit to search every space.")] string? spaceKey,
        [Description("Only issues with this exact status name (e.g. 'In Progress'). Omit to include every status.")] string? statusName,
        [Description("Only issues assigned to a user whose display name contains this text. Omit to include every assignee.")] string? assigneeName,
        [Description("Zero-based page number. Omit or pass 0 for the first page; pass the previous result's page + 1 to get the next page.")] int? page,
        [Description("Max issues to return per page, 1-50. Omit for the default of 50.")] int? count,
        CancellationToken cancellationToken)
    {
        return issueMcpService.ListIssues(GetAuthData(), spaceKey, statusName, assigneeName, page, count, cancellationToken);
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
    [Description("Updates an issue's status by id. Call list_statuses first to find the target status's id.")]
    public Task UpdateIssueStatus(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        [Description("The target status's id, from list_statuses. Not necessarily in the issue's current epic - moving to a different epic's status is allowed, same as the web UI.")] long statusId,
        CancellationToken cancellationToken)
    {
        return issueMcpService.UpdateIssueStatus(GetAuthData(), issueKey, statusId, cancellationToken);
    }

    [McpServerTool]
    [Description("Creates a new issue in a space and returns its key.")]
    public Task<string> CreateIssue(
        [Description("The space to create the issue in, e.g. 'BRD'.")] string spaceKey,
        [Description("The issue's text content.")] string content,
        [Description("The status id to create the issue in, from list_statuses - must belong to spaceKey.")] long statusId,
        [Description("Attribute name -> plain-text value, e.g. {\"Priority\": \"High\"}. Call list_attributes to see what's available and the expected value format per type. Omit to leave every attribute unset.")] IReadOnlyDictionary<string, string>? attributes,
        CancellationToken cancellationToken)
    {
        return issueMcpService.CreateIssue(GetAuthData(), spaceKey, content, statusId, attributes, cancellationToken);
    }

    [McpServerTool]
    [Description("Replaces an issue's text content.")]
    public Task EditIssue(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        [Description("The issue's new text content, replacing what's there now.")] string content,
        [Description("Attribute name -> plain-text value, same as create_issue. Omit to leave every attribute untouched (not cleared).")] IReadOnlyDictionary<string, string>? attributes,
        CancellationToken cancellationToken)
    {
        return issueMcpService.EditIssue(GetAuthData(), issueKey, content, attributes, cancellationToken);
    }

    [McpServerTool]
    [Description("Lists the statuses available in a space, grouped by epic - the ids create_issue/update_issue_status accept.")]
    public Task<IReadOnlyList<EpicStatusSummary>> ListStatuses(
        [Description("The space to list statuses for, e.g. 'BRD'.")] string spaceKey,
        CancellationToken cancellationToken)
    {
        return issueMcpService.ListStatuses(GetAuthData(), spaceKey, cancellationToken);
    }

    [McpServerTool]
    [Description("Lists the caller's organization's custom issue attributes (name, type, and allowed values for list-typed ones) - what create_issue/edit_issue's attributes map accepts.")]
    public Task<IReadOnlyList<AttributeSummary>> ListAttributes(CancellationToken cancellationToken)
    {
        return issueMcpService.ListAttributes(GetAuthData(), cancellationToken);
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
