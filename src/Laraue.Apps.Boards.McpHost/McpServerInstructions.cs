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
        "Use list_issues to find issues by space/status/assignee, and move_issue_status to " +
        "change an issue's status.";
}
