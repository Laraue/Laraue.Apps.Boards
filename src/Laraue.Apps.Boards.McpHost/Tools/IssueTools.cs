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
    [Description("Lists issues in the caller's organization, optionally filtered by space key, status id, or assignee id. Returns at most 50 issues per page (fewer if count is given), most recently updated first. Check the result's hasNextPage to know whether to request another page.")]
    public Task<IssueListPage> ListIssues(
        [Description("Only issues in this space (e.g. 'BRD'), from list_spaces. Omit to search every space.")] string? spaceKey = null,
        [Description("Only issues with this exact status id, from list_statuses. Omit to include every status.")] long? statusId = null,
        [Description("Only issues assigned to this user id, from list_members. Omit to include every assignee.")] Guid? assigneeId = null,
        [Description("Zero-based page number. Omit or pass 0 for the first page; pass the previous result's page + 1 to get the next page.")] int? page = null,
        [Description("Max issues to return per page, 1-50. Omit for the default of 50.")] int? count = null,
        CancellationToken cancellationToken = default)
    {
        return issueMcpService.ListIssues(GetAuthData(), spaceKey, statusId, assigneeId, page, count, cancellationToken);
    }

    [McpServerTool]
    [Description("Gets one issue's full content, comments and attachments by its key (e.g. 'BRD-42'). Each attachment's id is what edit_issue's removeAttachmentIds takes.")]
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
    [Description("Creates a new issue and returns its key. The destination space is derived entirely from statusId, same as the web app.")]
    public Task<string> CreateIssue(
        [Description("The issue's text content.")] string content,
        [Description("The status id to create the issue in, from list_statuses - this also determines which space the issue lands in.")] long statusId,
        [Description("Attribute id -> plain-text value, e.g. {\"5\": \"7\"}. Call list_attributes first for the ids/types/expected format per attribute - for a List-typed attribute, the value is one of its list value ids (also from list_attributes), not its display text. Omit to leave every attribute unset.")] IReadOnlyDictionary<long, string>? attributes = null,
        [Description("Files to attach, base64-encoded. Only image/jpeg, image/jpg and image/png are supported, max 3MB each.")] IReadOnlyList<FileAttachment>? files = null,
        CancellationToken cancellationToken = default)
    {
        return issueMcpService.CreateIssue(GetAuthData(), content, statusId, attributes, files, cancellationToken);
    }

    [McpServerTool]
    [Description("Fully replaces an issue's text content - not an append or merge. To keep any of the existing text, call get_issue first and include it in the new content you send. Does not change status or assignee.")]
    public Task EditIssue(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        [Description("The issue's complete new text content. Fully replaces the existing content - fetch it via get_issue first if you need to preserve any of it.")] string content,
        [Description("Attribute id -> plain-text value, e.g. {\"5\": \"7\"}. Call list_attributes first for the ids/types/expected format per attribute - for a List-typed attribute, the value is one of its list value ids (also from list_attributes), not its display text. Omit entirely to leave every attribute untouched; pass an empty object {} to clear every attribute the issue currently has.")] IReadOnlyDictionary<long, string>? attributes = null,
        [Description("Files to attach, in addition to the issue's existing ones. Same base64 format as create_issue.")] IReadOnlyList<FileAttachment>? files = null,
        [Description("Ids of existing attachments to remove, from get_issue's Attachments list. Can be combined with files to replace an attachment with another.")] IReadOnlyList<Guid>? removeAttachmentIds = null,
        CancellationToken cancellationToken = default)
    {
        return issueMcpService.EditIssue(GetAuthData(), issueKey, content, attributes, files, removeAttachmentIds, cancellationToken);
    }

    [McpServerTool]
    [Description("Lists the spaces available to the caller - the keys list_issues' spaceKey filter and list_statuses accept.")]
    public Task<IReadOnlyList<SpaceSummary>> ListSpaces(CancellationToken cancellationToken)
    {
        return issueMcpService.ListSpaces(GetAuthData(), cancellationToken);
    }

    [McpServerTool]
    [Description("Lists the statuses available in a space, grouped by epic - the ids create_issue/update_issue_status accept.")]
    public Task<IReadOnlyList<EpicStatusSummary>> ListStatuses(
        [Description("The space to list statuses for, e.g. 'BRD', from list_spaces.")] string spaceKey,
        CancellationToken cancellationToken)
    {
        return issueMcpService.ListStatuses(GetAuthData(), spaceKey, cancellationToken);
    }

    [McpServerTool]
    [Description("Lists the caller's organization's custom issue attributes (id, name, type, and allowed values with their own ids for list-typed ones) - what create_issue/edit_issue's attributes map accepts, keyed by id.")]
    public Task<IReadOnlyList<AttributeSummary>> ListAttributes(CancellationToken cancellationToken)
    {
        return issueMcpService.ListAttributes(GetAuthData(), cancellationToken);
    }

    [McpServerTool]
    [Description("Lists organization members visible to the caller - the ids list_issues' assigneeId filter accepts.")]
    public Task<IReadOnlyList<MemberSummary>> ListMembers(CancellationToken cancellationToken)
    {
        return issueMcpService.ListMembers(GetAuthData(), cancellationToken);
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
