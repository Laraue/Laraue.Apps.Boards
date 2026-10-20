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

    /// <summary>
    /// The unique index allowing one <see cref="Models.IssueLink"/> per pair of issues, whichever is the
    /// source. An expression index (LEAST/GREATEST of the two ids), created by raw SQL in the
    /// AddIssueLinks migration, since EF Core can't model one.
    /// </summary>
    public const string IssueLinkPairIndexName = "ux_issue_links_issue_pair";

    /// <summary>
    /// <c>XXXX-XXXX</c>: two groups of four, see <see cref="Models.AgentSignInRequest.UserCode"/>.
    /// </summary>
    public const int AgentSignInUserCodeLength = 9;

    /// <summary>
    /// Leaves room for the key name around it, "Claude Code agent (...)", within the key name's 64 characters.
    /// </summary>
    public const int MaxAgentSignInMachineNameLength = 44;
}