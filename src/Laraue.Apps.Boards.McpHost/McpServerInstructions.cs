namespace Laraue.Apps.Boards.McpHost;

/// <summary>
/// Text sent to every MCP client at connect time (<c>McpServerOptions.ServerInstructions</c>, set in
/// <c>Program.cs</c>). An overview of how the tools fit together - per-tool details (parameters, flags,
/// limits) belong in each tool's own description, not here.
/// </summary>
public static class McpServerInstructions
{
    public const string Text =
        """
        Laraue Boards: read and update issues in the caller's organization. Issues live in spaces
        (keyed like 'BRD'); an issue key looks like 'BRD-42'.

        - When the user mentions an issue, fetch it with get_issue instead of asking them to paste it.
          Its comments come from list_issue_comments, its change history from get_issue_history.
        - Find issues with list_issues (by space, epic, status or assignee). get_me returns your own user id,
          e.g. to list issues assigned to you.
        - Tools take ids, not names: space keys from list_spaces, epic ids from list_epics, status ids
          from list_statuses, attribute ids and allowed values from list_attributes, user ids from
          list_members.
        - Before changing something, check the permission flags you already have: canCreateIssue
          (list_spaces), canEdit/canDelete (list_issues, get_issue), canManage (list_issue_comments).
        - Every issue has a title, and create_issue/edit_issue both require one (a short one-line summary) -
          pass the issue's current title to edit_issue to keep it.
        - edit_issue replaces the whole content - read the issue first to keep what should stay.
        - Attachments: create_issue/edit_issue add files, edit_issue removes them, get_attachment
          downloads one by its id from get_issue or list_issue_comments.
        - When you mention an issue to the user, link it with its url from list_issues/get_issue.
        - A failed call says why ('NotFound: ...', 'Forbidden: ...', or 'BadRequest: ...' with a line
          per invalid field) - fix the request instead of retrying it unchanged.
        """;
}
