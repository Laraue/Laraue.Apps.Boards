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
        - Statuses are per epic and named freely; each has a category saying what it means: Created,
          InProgress, Review (done, waiting to be checked) or Completed. list_issues' statusCategoryId
          filter works across epics whatever their statuses are named.
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
        - Issues can be linked: one blocks another (it must be done first), relates to it, or duplicates it.
          get_issue lists an issue's links; list_issues with excludeBlocked=true leaves out blocked issues (a
          blocker counts until it's Completed - Review still blocks). create_issue_link/delete_issue_link add or remove a link.
        - Agent runs hand issues to a coding agent: the issue's assignee hands one off with create_agent_run
          (state Ready), which locks the issue's fields until the run ends. A runner, acting as the assignee,
          polls list_agent_runs(state 'Ready', assigneeIds=[its own user id], excludeBlocked=true), takes
          a run with claim_agent_run (false = someone else has it, or it's blocked: skip), moves the issue
          to an InProgress status, works, and ends with finish_agent_run (Done or Failed). Anyone who can
          edit the issue can cancel_agent_run, which also unlocks it; a runner whose finish_agent_run
          returns false was cancelled and should stop. get_issue's latestAgentRun shows the current one.
        - When you mention an issue to the user, link it with its url from list_issues/get_issue.
        - A failed call says why ('NotFound: ...', 'Forbidden: ...', or 'BadRequest: ...' with a line
          per invalid field) - fix the request instead of retrying it unchanged.
        """;
}
