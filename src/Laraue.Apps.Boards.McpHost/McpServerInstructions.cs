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
        "Use list_issues to find issues by space/status/assignee, update_issue_status to change " +
        "an issue's status, create_issue to add a new issue to a space, edit_issue to replace an " +
        "issue's content, and add_comment/edit_comment for its comments. get_issue's comment " +
        "list includes each comment's id, needed by edit_comment - only the comment's own author " +
        "can edit it. update_issue_status/create_issue take a status id, not a name - call " +
        "list_statuses first to find it. Before calling create_issue/edit_issue with an " +
        "attributes value, call list_attributes to see the exact names/types/allowed-values " +
        "rather than guessing them. list_issues' assigneeId filter takes a user id - call " +
        "list_members first to find one rather than guessing. list_spaces lists the space keys " +
        "list_issues/list_statuses accept. get_issue's Attachments list includes each " +
        "attachment's id - call get_attachment with it to download that attachment's original " +
        "file content, e.g. to look at an image the user attached.";
}
