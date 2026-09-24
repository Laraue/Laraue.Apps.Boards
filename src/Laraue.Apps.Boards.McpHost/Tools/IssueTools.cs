using System.ComponentModel;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.McpHost.Services;
using Laraue.Apps.Boards.Services;
using Laraue.Core.Exceptions.Web;
using ModelContextProtocol.Protocol;
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
    [Description("Lists issues in the caller's organization, optionally filtered by space, status id, or assignee id. Returns up to 50 per page, most recently updated first - check hasNextPage for more. Each issue's canEdit/canDelete reflect the caller's actual permissions on it.")]
    public Task<IssueListPage> ListIssues(
        [Description("Only issues in this space (e.g. 'BRD'), from list_spaces. Omit to search every space.")] string? spaceKey = null,
        [Description("Only issues with this exact status id, from list_statuses. Omit to include every status.")] long? statusId = null,
        [Description("Only issues assigned to this user id, from list_members. Omit to include every assignee.")] Guid? assigneeId = null,
        [Description("Zero-based page number - pass the previous result's page + 1 for the next page. Omit for the first page.")] int? page = null,
        [Description("Max issues per page, 1-50. Omit for the default of 50.")] int? count = null,
        CancellationToken cancellationToken = default)
    {
        return issueMcpService.ListIssues(GetAuthData(), spaceKey, statusId, assigneeId, page, count, cancellationToken);
    }

    [McpServerTool]
    [Description("Gets one issue's full content, comments and attachments by its key (e.g. 'BRD-42'). Includes canEdit/canDelete, each comment's id and canManage (for edit_comment/delete_comment), and each attachment's id (for edit_issue's removeAttachmentIds parameter).")]
    public Task<IssueDetail> GetIssue(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        CancellationToken cancellationToken)
    {
        return issueMcpService.GetIssue(GetAuthData(), issueKey, cancellationToken);
    }

    [McpServerTool]
    [Description("Moves an issue to a different status by id, optionally posting a comment in the same call. Requires canEdit (see get_issue/list_issues).")]
    public Task EditIssueStatus(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        [Description("The target status's id, from list_statuses. Can belong to a different epic than the issue's current one.")] long statusId,
        [Description("A comment to post on the issue as part of this status change, e.g. explaining why. Omit to move the status with no comment.")] string? comment = null,
        CancellationToken cancellationToken = default)
    {
        return issueMcpService.EditIssueStatus(GetAuthData(), issueKey, statusId, comment, cancellationToken);
    }

    [McpServerTool]
    [Description("Creates a new issue in the space the given status belongs to, and returns its key. Requires canCreateIssue on that space (see list_spaces).")]
    public Task<string> CreateIssue(
        [Description("The issue's text content.")] string content,
        [Description("The status to create the issue in, from list_statuses - this also determines the destination space.")] long statusId,
        [Description("User id to assign the issue to, from list_members. Must belong to the caller's organization. Omit to assign yourself.")] Guid? assigneeId = null,
        [Description("Attribute id -> plain-text value, e.g. {\"5\": \"7\"}. See list_attributes for ids/types/allowed values - for a List attribute, the value is one of its list value ids. Omit to leave every attribute unset.")] IReadOnlyDictionary<long, string>? attributes = null,
        [Description("Files to attach, base64-encoded - each becomes an attachment on the issue. Only image/jpeg, image/jpg and image/png; max 3MB each.")] IReadOnlyList<FileAttachment>? files = null,
        CancellationToken cancellationToken = default)
    {
        return issueMcpService.CreateIssue(GetAuthData(), content, statusId, assigneeId, attributes, files, cancellationToken);
    }

    [McpServerTool]
    [Description("Fully replaces an issue's text content - not an append or merge. Fetch the existing content via get_issue first if you need to preserve any of it. Does not change status. Requires canEdit (see get_issue/list_issues).")]
    public Task EditIssue(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        [Description("The issue's complete new text content, replacing what's there now.")] string content,
        [Description("User id to reassign the issue to, from list_members. Must belong to the caller's organization. Omit to leave the current assignee unchanged.")] Guid? assigneeId = null,
        [Description("Attribute id -> plain-text value, same format as create_issue. Omit to leave every attribute untouched; pass {} to clear all of them.")] IReadOnlyDictionary<long, string>? attributes = null,
        [Description("Files to attach, base64-encoded - each becomes a new attachment, in addition to the issue's existing ones. Same format as create_issue.")] IReadOnlyList<FileAttachment>? files = null,
        [Description("Ids of existing attachments to remove, from get_issue's Attachments list. Can be combined with files to replace one attachment with another.")] IReadOnlyList<Guid>? removeAttachmentIds = null,
        CancellationToken cancellationToken = default)
    {
        return issueMcpService.EditIssue(GetAuthData(), issueKey, content, assigneeId, attributes, files, removeAttachmentIds, cancellationToken);
    }

    [McpServerTool]
    [Description("Deletes an issue - it stops appearing anywhere except its own audit history. Requires canDelete (see get_issue/list_issues).")]
    public Task DeleteIssue(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        CancellationToken cancellationToken)
    {
        return issueMcpService.DeleteIssue(GetAuthData(), issueKey, cancellationToken);
    }

    [McpServerTool]
    [Description("Lists the spaces available to the caller - the keys list_issues' spaceKey filter and list_statuses accept. Each space's canCreateIssue reflects the caller's actual permission there.")]
    public Task<IReadOnlyList<SpaceSummary>> ListSpaces(CancellationToken cancellationToken)
    {
        return issueMcpService.ListSpaces(GetAuthData(), cancellationToken);
    }

    [McpServerTool]
    [Description("Lists the statuses in a space, grouped by epic - the ids create_issue/edit_issue_status accept.")]
    public Task<IReadOnlyList<EpicStatusSummary>> ListStatuses(
        [Description("The space to list statuses for, e.g. 'BRD', from list_spaces.")] string spaceKey,
        CancellationToken cancellationToken)
    {
        return issueMcpService.ListStatuses(GetAuthData(), spaceKey, cancellationToken);
    }

    [McpServerTool]
    [Description("Lists the caller's organization's custom issue attributes - id, name, type, and (for List-typed ones) allowed values with their own ids. What create_issue/edit_issue's attributes map accepts, keyed by id.")]
    public Task<IReadOnlyList<AttributeSummary>> ListAttributes(CancellationToken cancellationToken)
    {
        return issueMcpService.ListAttributes(GetAuthData(), cancellationToken);
    }

    [McpServerTool]
    [Description("Lists organization members visible to the caller - the ids list_issues' assigneeId filter, and create_issue/edit_issue's assigneeId, accept.")]
    public Task<IReadOnlyList<MemberSummary>> ListMembers(
        [Description("Only members visible in this space, from list_spaces. Pass it when picking an assignee for that space, so they're guaranteed to see issues there. Omit to list every member visible anywhere.")] string? spaceKey = null,
        CancellationToken cancellationToken = default)
    {
        return issueMcpService.ListMembers(GetAuthData(), spaceKey, cancellationToken);
    }

    [McpServerTool]
    [Description("Downloads an issue attachment's original file content, by the id from get_issue's Attachments list. Only image attachments are supported today.")]
    public async Task<CallToolResult> GetAttachment(
        [Description("The attachment's id, from get_issue's Attachments list.")] Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var content = await issueMcpService.GetAttachmentContent(GetAuthData(), attachmentId, cancellationToken);
        await using var stream = content.Content;

        var bytes = await ReadBoundedAsync(stream, SystemMimeTypes.MaxFileSizeBytes, cancellationToken);

        return new CallToolResult
        {
            Content = [new ImageContentBlock { Data = bytes, MimeType = content.MimeType }],
        };
    }

    /// <summary>
    /// Copies <paramref name="stream"/> into memory, aborting as soon as it's read more than
    /// <paramref name="maxBytes"/> - a hard runtime cap on top of
    /// <see cref="IIssueMcpService.GetAttachmentContent"/>'s own DB-size-based check, so an
    /// attachment whose recorded size is missing or wrong still can't make this tool buffer an
    /// unbounded amount of memory building the resulting <see cref="ImageContentBlock"/>.
    /// </summary>
    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        var buffer = new byte[81920];

        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memoryStream.Length + bytesRead > maxBytes)
                throw new BadRequestException("attachmentId", $"Attachment content exceeds the {maxBytes}-byte download limit.");

            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return memoryStream.ToArray();
    }

    [McpServerTool]
    [Description("Creates a comment on an issue and returns its id.")]
    public Task<long> CreateComment(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        [Description("The comment's text.")] string text,
        CancellationToken cancellationToken)
    {
        return issueMcpService.CreateComment(GetAuthData(), issueKey, text, cancellationToken);
    }

    [McpServerTool]
    [Description("Edits a comment's text. Only the comment's own author can edit it - check canManage on get_issue's comment list before calling.")]
    public Task EditComment(
        [Description("The comment's id, from get_issue's comment list.")] long commentId,
        [Description("The comment's new text, replacing what's there now.")] string text,
        CancellationToken cancellationToken)
    {
        return issueMcpService.EditComment(GetAuthData(), commentId, text, cancellationToken);
    }

    [McpServerTool]
    [Description("Deletes a comment - it stops appearing anywhere except the issue's audit history, and this cannot be undone through this API. Only the comment's own author can delete it - check canManage on get_issue's comment list before calling. To just change its text instead, use edit_comment.")]
    public Task DeleteComment(
        [Description("The comment's id, from get_issue's comment list.")] long commentId,
        CancellationToken cancellationToken)
    {
        return issueMcpService.DeleteComment(GetAuthData(), commentId, cancellationToken);
    }

    private OrganizationAuthData GetAuthData()
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new UnauthorizedException("No HTTP context available to resolve the caller's identity.");

        return httpContext.User.GetOrganizationAuthData();
    }
}
