using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.McpHost.Services;
using Laraue.Core.Exceptions.Web;
using ModelContextProtocol.Server;

namespace Laraue.Apps.Boards.McpHost.Tools;

/// <summary>
/// MCP tools for agent runs (BRD-305) - handing issues to an agent, and the runner's poll/claim/finish loop. A thin
/// adapter like <see cref="IssueTools"/>; the logic lives in <see cref="IAgentRunMcpService"/>.
/// </summary>
[McpServerToolType]
public class AgentRunTools(IAgentRunMcpService agentRunMcpService, IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool]
    [Description("Hands an issue to the agent: creates an agent run in state Ready, which a runner then claims with claim_agent_run. An issue can have only one open run (Ready or Working) - cancel it first to hand the issue off again. Returns the run. Requires canEdit (see get_issue/list_issues).")]
    public Task<AgentRunSummary> CreateAgentRun(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        CancellationToken cancellationToken)
    {
        return agentRunMcpService.CreateAgentRun(GetAuthData(), issueKey, cancellationToken);
    }

    [McpServerTool]
    [Description("Lists agent runs of the issues the caller can read, oldest first, up to 50 per page - check hasNextPage for more. A runner polls state 'Ready' and takes the first run it can claim. Each run has its id, the issue (key, title, url), state (Ready, Working, Done, Failed or Cancelled), who handed it off and when, the API key that claimed it, and when it finished and why it failed.")]
    public Task<AgentRunListPage> ListAgentRuns(
        [Description("Only runs of issues in this space (e.g. 'BRD'), from list_spaces. Omit to include every space.")] string? spaceKey = null,
        [Description("Only runs in this state: Ready, Working, Done, Failed or Cancelled. Omit to include every state.")] string? state = null,
        [Description("Only runs of issues assigned to one of these user ids, from list_members or get_me - e.g. a runner passes its own user id to take only its owner's issues. Omit to include every assignee.")] IReadOnlyList<Guid>? assigneeIds = null,
        [Description("Zero-based page number - pass the previous result's page + 1 for the next page. Omit for the first page.")] int? page = null,
        [Description("Max runs per page, 1-50. Omit for the default of 50.")] int? count = null,
        CancellationToken cancellationToken = default)
    {
        return agentRunMcpService.ListAgentRuns(GetAuthData(), spaceKey, state, assigneeIds, page, count, cancellationToken);
    }

    [McpServerTool]
    [Description("Claims a Ready agent run for this runner, moving it to Working. Returns true when this call claimed it, false when it was no longer Ready (another runner claimed it first, or it was cancelled) - on false, skip the run. Atomic: of two runners claiming the same run, exactly one gets true. After claiming, move the issue to its epic's first InProgress status with edit_issue_status. Requires canEdit on the issue.")]
    public Task<bool> ClaimAgentRun(
        [Description("The run's id, from list_agent_runs.")] long runId,
        CancellationToken cancellationToken)
    {
        return agentRunMcpService.ClaimAgentRun(GetAuthData(), runId, cancellationToken);
    }

    [McpServerTool]
    [Description("Finishes a Working agent run as Done or Failed. Returns true when the run was finished, false when it was no longer Working - a person cancelled it, so stop working on it. Requires canEdit on the issue.")]
    public Task<bool> FinishAgentRun(
        [Description("The run's id, from list_agent_runs.")] long runId,
        [Description("'Done' when the work is finished, 'Failed' when the agent gave up.")] string state,
        [Description("Why the run failed, max 4096 characters - kept with a Failed run, ignored for Done. Omit for Done.")] [MaxLength(Constraints.MaxAgentRunFailureReasonLength)] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        return agentRunMcpService.FinishAgentRun(GetAuthData(), runId, state, reason, cancellationToken);
    }

    [McpServerTool]
    [Description("Cancels an open (Ready or Working) agent run: a Ready run is no longer offered to runners, and the runner working on a Working run sees it is no longer Working and stops. Returns false when the run had already finished or was cancelled. Requires canEdit on the issue.")]
    public Task<bool> CancelAgentRun(
        [Description("The run's id, from list_agent_runs or get_issue's latestAgentRun.")] long runId,
        CancellationToken cancellationToken)
    {
        return agentRunMcpService.CancelAgentRun(GetAuthData(), runId, cancellationToken);
    }

    private OrganizationAuthData GetAuthData()
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new UnauthorizedException("No HTTP context available to resolve the caller's identity.");

        return httpContext.User.GetOrganizationAuthData();
    }
}
