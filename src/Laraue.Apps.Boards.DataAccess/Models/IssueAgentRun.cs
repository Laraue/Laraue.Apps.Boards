using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.DataAccess.Enums;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// One hand-off of an issue to an agent (BRD-305): a person creates it as <see cref="AgentRunState.Ready"/>,
/// a runner claims it (<see cref="AgentRunState.Working"/>) and finishes it as Done or Failed, or a person
/// cancels it. A retry is a new row, so an issue keeps the history of its runs. At most one run per issue
/// is open (Ready or Working) - a partial unique index, <see cref="Constraints.OpenAgentRunIndexName"/>.
/// </summary>
public class IssueAgentRun
{
    public long Id { get; set; }

    public long IssueId { get; set; }
    public Issue? Issue { get; set; }

    public AgentRunState State { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Who handed the issue off.
    /// </summary>
    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public DateTime? ClaimedAt { get; set; }

    /// <summary>
    /// The runner that claimed the run, by the API key it called with.
    /// </summary>
    public Guid? ClaimedByApiKeyId { get; set; }
    public ApiKey? ClaimedByApiKey { get; set; }

    /// <summary>
    /// When the run reached Done, Failed or Cancelled.
    /// </summary>
    public DateTime? FinishedAt { get; set; }

    [MaxLength(Constraints.MaxAgentRunFailureReasonLength)]
    public string? FailureReason { get; set; }
}
