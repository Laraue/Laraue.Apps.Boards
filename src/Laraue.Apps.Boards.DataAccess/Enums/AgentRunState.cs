namespace Laraue.Apps.Boards.DataAccess.Enums;

/// <summary>
/// Where an <see cref="Models.IssueAgentRun"/> is. The values are stored in the database (and in the
/// partial unique index on open runs, see <see cref="Constraints.OpenAgentRunIndexName"/>), so an
/// existing value never changes its number.
/// </summary>
public enum AgentRunState
{
    /// <summary>
    /// A person handed the issue to the agent; no runner has claimed it yet.
    /// </summary>
    Ready = 0,

    /// <summary>
    /// A runner claimed the run and is working on the issue.
    /// </summary>
    Working = 1,

    /// <summary>
    /// The runner finished the work.
    /// </summary>
    Done = 2,

    /// <summary>
    /// The runner gave up; see <see cref="Models.IssueAgentRun.FailureReason"/>.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// A person stopped the run before it finished.
    /// </summary>
    Cancelled = 4,
}
