using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
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
    [Description("Hands an issue to the agent: creates an agent run in state Ready, which a runner then claims with claim_agent_run. An issue can have only one open run (Ready or Working) - cancel it first to hand the issue off again. A blocked issue can be handed off: its run waits (isBlocked) until every issue blocking it is Completed. While the run is open the issue is locked: edit_issue and delete_issue fail until it's finished or cancelled (status changes and comments still work). Returns the run. Only the issue's assignee can hand it off, with canEdit (see get_issue/list_issues); the issue must be in a Created status, and its epic needs Created, InProgress and Review statuses (see list_statuses) - the agent moves it through them.")]
    public Task<AgentRunSummary> CreateAgentRun(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        CancellationToken cancellationToken)
    {
        return agentRunMcpService.CreateAgentRun(GetAuthData(), issueKey, cancellationToken);
    }

    [McpServerTool]
    [Description("Lists agent runs of the issues the caller can read, oldest first, up to 50 per page - check hasNextPage for more. A runner polls state 'Ready' with excludeBlocked=true and takes the first run it can claim. Each run has its id, the issue (key, title, url), state (Ready, Working, Done, Failed or Cancelled), isBlocked (an issue blocking it isn't Completed yet - see get_issue's links), who handed it off and when, the API key that claimed it, and when it finished and why it failed.")]
    public Task<AgentRunListPage> ListAgentRuns(
        [Description("Only runs of issues in this space (e.g. 'BRD'), from list_spaces. Omit to include every space.")] string? spaceKey = null,
        [Description("Only runs in this state. Omit to include every state.")] AgentRunState? state = null,
        [Description("Only runs of issues assigned to one of these user ids, from list_members or get_me - e.g. a runner passes its own user id to take only its owner's issues. Omit to include every assignee.")] IReadOnlyList<Guid>? assigneeIds = null,
        [Description("Pass true to leave out runs of blocked issues - an issue is blocked while an issue that blocks it is in Created, InProgress or Review; only Completed releases it. Blockers you can't read don't count. A runner should pass true: a blocked run can't be claimed. Omit to include them.")] bool excludeBlocked = false,
        [Description("Zero-based page number - pass the previous result's page + 1 for the next page. Omit for the first page.")] int? page = null,
        [Description("Max runs per page, 1-50. Omit for the default of 50.")] int? count = null,
        CancellationToken cancellationToken = default)
    {
        return agentRunMcpService.ListAgentRuns(GetAuthData(), spaceKey, state, assigneeIds, excludeBlocked, page, count, cancellationToken);
    }

    [McpServerTool]
    [Description("Claims a Ready agent run for this runner, moving it to Working. Returns true when this call claimed it, false when it was no longer Ready (another runner claimed it first, or it was cancelled) or its issue is blocked - on false, skip the run. Atomic: of two runners claiming the same run, exactly one gets true. Only the issue's assignee can claim it (a runner works only on its own user's issues). After claiming, move the issue to its epic's first InProgress status with edit_issue_status. Requires canEdit on the issue.")]
    public Task<bool> ClaimAgentRun(
        [Description("The run's id, from list_agent_runs.")] long runId,
        CancellationToken cancellationToken)
    {
        return agentRunMcpService.ClaimAgentRun(GetAuthData(), runId, cancellationToken);
    }

    [McpServerTool]
    [Description("Starts an agent run that is already Working, for an agent working on an issue together with a person outside a hand-off (e.g. a runner's interactive session) - so the issue shows the agent on it, and the work ends with finish_agent_run (Done or Failed) or cancel_agent_run like any run. Unlike create_agent_run, the issue may be in any status and its epic needs no particular statuses; the issue's status isn't changed. Fails when the issue already has an open run (Ready or Working). While the run is open the issue is locked: edit_issue and delete_issue fail (status changes and comments still work). Returns the run. Only the issue's assignee can start one, with canEdit (see get_issue/list_issues).")]
    public Task<AgentRunSummary> StartAgentRun(
        [Description("The issue's key, e.g. 'BRD-42'.")] string issueKey,
        CancellationToken cancellationToken)
    {
        return agentRunMcpService.StartAgentRun(GetAuthData(), issueKey, cancellationToken);
    }

    [McpServerTool]
    [Description("Finishes a Working agent run as Done or Failed. Returns true when the run was finished, false when it was no longer Working - a person cancelled it, so stop working on it. Requires canEdit on the issue.")]
    public Task<bool> FinishAgentRun(
        [Description("The run's id, from list_agent_runs.")] long runId,
        [Description("Done when the work is finished, Failed when the agent gave up.")] AgentRunFinishState state,
        [Description("Why the run failed, max 4096 characters - kept with a Failed run, ignored for Done. Omit for Done.")] [MaxLength(Constraints.MaxAgentRunFailureReasonLength)] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        return agentRunMcpService.FinishAgentRun(GetAuthData(), runId, state, reason, cancellationToken);
    }

    [McpServerTool]
    [Description("Cancels an open (Ready or Working) agent run: a Ready run is no longer offered to runners, and the runner working on a Working run sees it is no longer Working and stops. Cancelling also unlocks the issue for editing. Returns false when the run had already finished or was cancelled. Requires canEdit on the issue.")]
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
