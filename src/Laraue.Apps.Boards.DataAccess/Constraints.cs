namespace Laraue.Apps.Boards.DataAccess;

public class Constraints
{
    public const int MaxCommentLength = 4096;

    public const int MaxTitleLength = 256;

    public const int MaxContentLength = 4096;

    public const string SpaceKeyIndexName = "ix_spaces_organization_id_key";

    public const int MaxAgentRunFailureReasonLength = 4096;

    /// <summary>
    /// Partial unique index allowing at most one open (Ready or Working) agent run per issue.
    /// </summary>
    public const string OpenAgentRunIndexName = "ix_issue_agent_runs_issue_id_open";
}