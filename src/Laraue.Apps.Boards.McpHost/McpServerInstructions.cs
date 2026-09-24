namespace Laraue.Apps.Boards.McpHost;

/// <summary>
/// Text sent to every MCP client at connect time (<c>McpServerOptions.ServerInstructions</c>, set
/// in <c>Program.cs</c>), so a session knows to reach for these tools - rather than guessing or
/// asking the user to paste content in - whenever it needs an issue's current status, its full
/// text/history, or to move it.
/// </summary>
public static class McpServerInstructions
{
    public const string Text =
        "Boards MCP server: use these tools to read and update issues in the caller's Laraue " +
        "Boards organization. To get an issue's current status, full text, or comment history, " +
        "call get_issue with its key (e.g. 'BRD-42') rather than asking the user to paste it. " +
        "Use list_issues to find issues by space/status/assignee, edit_issue_status to change " +
        "an issue's status, create_issue to add a new issue to a space, edit_issue to replace " +
        "an issue's content, delete_issue to remove one, and create_comment/edit_comment/" +
        "delete_comment for its comments. get_issue's comment list includes each comment's id " +
        "and canManage, needed by edit_comment/delete_comment - only the comment's own author " +
        "can edit or delete it, and canManage tells you upfront whether the caller is that " +
        "author. There's no separate attachment tool: create_issue/edit_issue's files " +
        "parameter attaches new files, and edit_issue's removeAttachmentIds parameter removes " +
        "existing ones by the id from get_issue's Attachments list. edit_issue_status/" +
        "create_issue take a status id, not a name - call list_statuses first to find it. " +
        "Before calling create_issue/edit_issue with an attributes value, call list_attributes " +
        "to see the exact names/types/allowed-values rather than guessing them. list_issues' " +
        "assigneeId filter, and create_issue/edit_issue's assigneeId, take a user id - call " +
        "list_members first to find one rather than guessing. When picking an assignee for a " +
        "specific space, pass that space's key as list_members' spaceKey so you only see " +
        "members who can actually see issues there. list_spaces lists the space keys " +
        "list_issues/list_statuses accept. get_issue's Attachments list includes each " +
        "attachment's id - call get_attachment with it to download that attachment's original " +
        "file content, e.g. to look at an image the user attached. list_issues/get_issue " +
        "return canEdit/canDelete per issue - check these before calling edit_issue/" +
        "edit_issue_status/delete_issue on it, rather than finding out from a permission error. " +
        "list_spaces returns canCreateIssue per space - check it before calling " +
        "list_statuses/create_issue there.";
}
