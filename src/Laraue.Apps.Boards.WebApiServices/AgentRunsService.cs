using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.WebApiServices;

/// <summary>
/// The web app's side of agent runs (BRD-305): "Hand to agent" and "Cancel" on an issue. Runners claim and finish
/// runs through the MCP host. Both actions need <c>CanUpdateIssue</c> on the issue; handing off also needs the
/// caller to be the issue's assignee.
/// </summary>
public interface IAgentRunsService
{
    Task<AgentRunDto> Create(
        CreateAgentRunRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);

    /// <summary>False when the run had already finished or was cancelled.</summary>
    Task<bool> Cancel(
        long runId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether the caller can hand the issue to the agent now, and if not, what stands in the way - the one place
    /// that decides, so the issue page shows exactly what <see cref="Create"/> would accept.
    /// <paramref name="accessLevels"/> are the caller's, already resolved for the issue.
    /// </summary>
    Task<AgentHandOffDto> GetHandOff(
        OrganizationAuthData authData,
        long issueId,
        AccessLevels accessLevels,
        CancellationToken cancellationToken);
}

public class AgentRunsService(
    DatabaseContext context,
    IAccessService accessService,
    ICoreAgentRunsService coreAgentRunsService,
    IApiKeysService apiKeysService,
    IAgentRunBoardRequirements agentRunBoardRequirements)
    : IAgentRunsService
{
    public async Task<AgentRunDto> Create(
        CreateAgentRunRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var key = new IssueKey(request.IssueKey);

        var issue = await accessService.GetIssueAccessByKey(authData, key, includeDeleted: false, cancellationToken);
        EnsureCanUpdateIssue(issue?.AccessLevels, "Issue", key.ToString());
        var issueId = issue!.IssueId;

        var handOff = await GetHandOff(authData, issueId, issue.AccessLevels, cancellationToken);
        switch (handOff.Blocker)
        {
            case AgentHandOffBlocker.NotAllowed:
                throw new ForbiddenException(string.Format(ErrorMessages.AgentRunHandOffOnlyAssignee, key));
            case AgentHandOffBlocker.NoApiKey:
                throw new BadRequestException("issueKey", ErrorMessages.AgentRunNeedsApiKey);
        }

        // An open run, a started issue or a board without the statuses: the core service refuses those itself
        // (MCP hand-offs go through it too), with its own messages.

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var runId = await coreAgentRunsService.Create(issueId, authData.ToActor(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await context.IssueAgentRuns
            .Where(r => r.Id == runId)
            .SelectAgentRunDto()
            .FirstAsync(cancellationToken);
    }

    public async Task<bool> Cancel(
        long runId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var issueId = await context.IssueAgentRuns
            .Where(r => r.Id == runId)
            .Where(r => r.Issue!.Status!.Epic!.Space!.OrganizationId == authData.OrganizationId)
            .Select(r => r.IssueId)
            .FirstOrThrowNotFoundEFAsync(
                string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Agent run", runId),
                cancellationToken);

        EnsureCanUpdateIssue(
            await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken),
            "Agent run",
            runId.ToString());

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var cancelled = await coreAgentRunsService.Cancel(runId, authData.ToActor(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return cancelled;
    }

    public async Task<AgentHandOffDto> GetHandOff(
        OrganizationAuthData authData,
        long issueId,
        AccessLevels accessLevels,
        CancellationToken cancellationToken)
    {
        var issue = await context.ActiveIssues()
            .Where(i => i.Id == issueId)
            .Select(i => new
            {
                i.AssigneeId,
                i.Status!.Category,
                i.Status.EpicId,
                HasOpenRun = i.AgentRuns!.Any(r => r.State == AgentRunState.Ready || r.State == AgentRunState.Working),
            })
            .FirstAsync(cancellationToken);

        // Only the assignee hands an issue to their own agent (see AgentRunMcpService.EnsureIsAssignee).
        if (!accessLevels.CanUpdateIssue || issue.AssigneeId != authData.UserId)
            return AgentHandOffDto.Blocked(AgentHandOffBlocker.NotAllowed);

        if (issue.HasOpenRun)
            return AgentHandOffDto.Blocked(AgentHandOffBlocker.RunOpen);

        // A runner signs in with one of the assignee's API keys; without any, nothing could ever claim the run.
        // (Through MCP the caller is already using a key.)
        if (!await apiKeysService.HasActiveApiKey(authData, cancellationToken))
            return AgentHandOffDto.Blocked(AgentHandOffBlocker.NoApiKey);

        var missingCategories = await agentRunBoardRequirements.GetMissingStatusCategories(issue.EpicId, cancellationToken);
        if (missingCategories.Length > 0)
            return AgentHandOffDto.Blocked(AgentHandOffBlocker.BoardNotReady, missingCategories);

        if (issue.Category != StatusCategory.Created)
            return AgentHandOffDto.Blocked(AgentHandOffBlocker.IssueStarted);

        return AgentHandOffDto.Allowed;
    }

    // 404 when the issue is missing or not readable, 403 when it's readable but the caller can't change it - see
    // AGENTS.md "Choosing 404 vs 403".
    private static void EnsureCanUpdateIssue(AccessLevels? accessLevels, string entityName, string entityId)
    {
        if (accessLevels is not { CanRead: true })
            throw new NotFoundException(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, entityName, entityId));

        if (!accessLevels.CanUpdateIssue)
            throw new ForbiddenException(string.Format(ErrorMessages.EntityActionForbidden, entityName, entityId, "update"));
    }
}

public record CreateAgentRunRequest
{
    public required string IssueKey { get; init; }
}

/// <summary>
/// An agent run of an issue. <see cref="ClaimedByApiKeyName"/> names the runner's API key once it claimed the run;
/// <see cref="FailureReason"/> is set for a Failed run.
/// </summary>
public record AgentRunDto
{
    public required long Id { get; init; }
    public required AgentRunState State { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime? ClaimedAt { get; init; }
    public required string? ClaimedByApiKeyName { get; init; }
    public required DateTime? FinishedAt { get; init; }
    public required string? FailureReason { get; init; }
}

/// <summary>
/// Whether the caller can hand an issue to the agent now (<see cref="IAgentRunsService.GetHandOff"/>).
/// <see cref="Blocker"/> says why not; <see cref="MissingStatusCategories"/> is the detail for
/// <see cref="AgentHandOffBlocker.BoardNotReady"/>.
/// </summary>
public record AgentHandOffDto
{
    public required bool Available { get; init; }
    public required AgentHandOffBlocker? Blocker { get; init; }
    public required StatusCategory[] MissingStatusCategories { get; init; }

    public static readonly AgentHandOffDto Allowed = new()
    {
        Available = true,
        Blocker = null,
        MissingStatusCategories = [],
    };

    public static AgentHandOffDto Blocked(AgentHandOffBlocker blocker, StatusCategory[]? missingStatusCategories = null)
    {
        return new AgentHandOffDto
        {
            Available = false,
            Blocker = blocker,
            MissingStatusCategories = missingStatusCategories ?? [],
        };
    }
}

public enum AgentHandOffBlocker
{
    /// <summary>The caller can't hand this issue off at all: only its assignee can, with edit rights.</summary>
    NotAllowed,

    /// <summary>The issue already has a run that's ready or working.</summary>
    RunOpen,

    /// <summary>The caller has no active API key for their agent runner to sign in with.</summary>
    NoApiKey,

    /// <summary>The issue's board lacks a status in some category the agent moves the issue through.</summary>
    BoardNotReady,

    /// <summary>The issue isn't in a not started status.</summary>
    IssueStarted,
}
