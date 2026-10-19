using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.Resources;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Agent runs of an issue (<see cref="IssueAgentRun"/>, BRD-305): hand-off, claim, finish, cancel. Every state
/// change is one conditional <c>UPDATE ... WHERE State = expected</c>, so two callers racing for the same run
/// can't both win - the loser gets <c>false</c>. Each change is written to the issue's history, attributed to
/// the <see cref="Actor"/> (with its API key, for a runner). No permission checks, like every core service; the
/// caller must have opened a transaction.
/// </summary>
public interface ICoreAgentRunsService
{
    /// <summary>
    /// Hands the issue off: creates a <see cref="AgentRunState.Ready"/> run. Throws
    /// <see cref="BadRequestException"/> when the issue already has an open (Ready or Working) run, isn't in a
    /// Created status, or its board misses a status category the agent needs
    /// (<see cref="AgentRunBoardRequirements"/>).
    /// Returns the new run's id.
    /// </summary>
    Task<long> Create(
        long issueId,
        Actor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ready -> Working. True when this call claimed the run, false when it wasn't Ready any more
    /// (someone else claimed it first, or it was cancelled), its board no longer has the statuses the agent needs
    /// (the issue moved, or a status was removed - the run then fails with that reason). Whether the issue is
    /// blocked depends on which blockers the caller can read, so the caller checks that first.
    /// </summary>
    Task<bool> Claim(
        long runId,
        Actor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Working -> <paramref name="state"/> (<see cref="AgentRunState.Done"/> or <see cref="AgentRunState.Failed"/>;
    /// <paramref name="failureReason"/> is kept for Failed only). False when the run wasn't Working - e.g. a
    /// person cancelled it meanwhile.
    /// </summary>
    Task<bool> Finish(
        long runId,
        AgentRunState state,
        string? failureReason,
        Actor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ready or Working -> Cancelled. False when the run was already finished or cancelled.
    /// </summary>
    Task<bool> Cancel(
        long runId,
        Actor actor,
        CancellationToken cancellationToken);
}

public class CoreAgentRunsService(
    DatabaseContext context,
    IIssueHistoryService issueHistoryService,
    IAgentRunBoardRequirements agentRunBoardRequirements,
    IDateTimeProvider dateTimeProvider)
    : ICoreAgentRunsService
{
    public async Task<long> Create(
        long issueId,
        Actor actor,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var issue = await context.ActiveIssues()
            .Where(x => x.Id == issueId)
            .Select(x => new { x.Status!.Category, x.Status.EpicId })
            .FirstAsync(cancellationToken);

        if (issue.Category != StatusCategory.Created)
            throw new BadRequestException("issueKey", ErrorMessages.AgentRunIssueStarted);

        var missingCategories = await agentRunBoardRequirements.GetMissingStatusCategories(issue.EpicId, cancellationToken);
        if (missingCategories.Length > 0)
            throw new BadRequestException("issueKey", BoardNotReadyMessage(missingCategories));

        var now = dateTimeProvider.UtcNow;

        var run = new IssueAgentRun
        {
            IssueId = issueId,
            State = AgentRunState.Ready,
            CreatedAt = now,
            CreatedByUserId = actor.UserId,
        };

        context.IssueAgentRuns.Add(run);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsOpenRunViolation(ex))
        {
            context.Entry(run).State = EntityState.Detached;
            throw new BadRequestException("issueKey", ErrorMessages.AgentRunAlreadyOpen);
        }

        await RecordStateChange(run.Id, null, AgentRunState.Ready, actor, now, cancellationToken);

        return run.Id;
    }

    public async Task<bool> Claim(
        long runId,
        Actor actor,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var now = dateTimeProvider.UtcNow;

        // Status changes stay allowed while a run is open, so the issue may have moved to a board the agent can't
        // finish on (or a status was removed): such a run fails instead of being claimed.
        var epicId = await context.IssueAgentRuns
            .Where(x => x.Id == runId)
            .Select(x => x.Issue!.Status!.EpicId)
            .FirstAsync(cancellationToken);

        var missingCategories = await agentRunBoardRequirements.GetMissingStatusCategories(epicId, cancellationToken);
        if (missingCategories.Length > 0)
        {
            var reason = BoardNotReadyMessage(missingCategories);

            var failed = await context.IssueAgentRuns
                .Where(x => x.Id == runId && x.State == AgentRunState.Ready)
                .ExecuteUpdateAsync(
                    u => u
                        .SetProperty(x => x.State, AgentRunState.Failed)
                        .SetProperty(x => x.FinishedAt, now)
                        .SetProperty(x => x.FailureReason, reason),
                    cancellationToken);

            if (failed > 0)
                await RecordStateChange(runId, AgentRunState.Ready, AgentRunState.Failed, actor, now, cancellationToken);

            return false;
        }

        var updated = await context.IssueAgentRuns
            .Where(x => x.Id == runId && x.State == AgentRunState.Ready)
            .ExecuteUpdateAsync(
                u => u
                    .SetProperty(x => x.State, AgentRunState.Working)
                    .SetProperty(x => x.ClaimedAt, now)
                    .SetProperty(x => x.ClaimedByApiKeyId, actor.ApiKeyId),
                cancellationToken);

        if (updated == 0)
            return false;

        await RecordStateChange(runId, AgentRunState.Ready, AgentRunState.Working, actor, now, cancellationToken);

        return true;
    }

    public async Task<bool> Finish(
        long runId,
        AgentRunState state,
        string? failureReason,
        Actor actor,
        CancellationToken cancellationToken)
    {
        if (state is not (AgentRunState.Done or AgentRunState.Failed))
            throw new BadRequestException("state", ErrorMessages.AgentRunFinishStateInvalid);

        context.Database.EnsureTransactionStarted();

        var now = dateTimeProvider.UtcNow;
        var reason = state == AgentRunState.Failed ? failureReason : null;

        var updated = await context.IssueAgentRuns
            .Where(x => x.Id == runId && x.State == AgentRunState.Working)
            .ExecuteUpdateAsync(
                u => u
                    .SetProperty(x => x.State, state)
                    .SetProperty(x => x.FinishedAt, now)
                    .SetProperty(x => x.FailureReason, reason),
                cancellationToken);

        if (updated == 0)
            return false;

        await RecordStateChange(runId, AgentRunState.Working, state, actor, now, cancellationToken);

        return true;
    }

    public async Task<bool> Cancel(
        long runId,
        Actor actor,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        // A run only moves Ready -> Working -> finished, so trying Ready first and then Working can't miss a run
        // that is claimed in between the two statements.
        foreach (var openState in (AgentRunState[])[AgentRunState.Ready, AgentRunState.Working])
        {
            var now = dateTimeProvider.UtcNow;

            var updated = await context.IssueAgentRuns
                .Where(x => x.Id == runId && x.State == openState)
                .ExecuteUpdateAsync(
                    u => u
                        .SetProperty(x => x.State, AgentRunState.Cancelled)
                        .SetProperty(x => x.FinishedAt, now),
                    cancellationToken);

            if (updated == 0)
                continue;

            await RecordStateChange(runId, openState, AgentRunState.Cancelled, actor, now, cancellationToken);

            return true;
        }

        return false;
    }

    private async Task RecordStateChange(
        long runId,
        AgentRunState? oldState,
        AgentRunState newState,
        Actor actor,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        var run = await context.IssueAgentRuns
            .Where(x => x.Id == runId)
            .Select(x => new
            {
                x.IssueId,
                x.Issue!.Status!.Epic!.Space!.OrganizationId,
            })
            .FirstAsync(cancellationToken);

        await issueHistoryService.Record(
            run.IssueId,
            LogEntityType.Issue,
            LogAction.Update,
            run.OrganizationId,
            actor,
            createdAt,
            [
                new OrganizationLogItem
                {
                    PropertyType = PropertyType.AgentRun,
                    ParentId = runId.ToString(),
                    OldValueId = oldState is null ? null : ((int)oldState).ToString(),
                    OldDisplayValue = oldState?.ToString(),
                    NewValueId = ((int)newState).ToString(),
                    NewDisplayValue = newState.ToString(),
                },
            ],
            cancellationToken);
    }

    private static string BoardNotReadyMessage(IEnumerable<StatusCategory> missingCategories)
    {
        return string.Format(ErrorMessages.AgentRunBoardNotReady, string.Join(", ", missingCategories));
    }

    // The partial unique index is the only race-free check, so a second open run is detected by the violation itself.
    private static bool IsOpenRunViolation(Exception ex)
    {
        var pgException = ex as PostgresException ?? ex.InnerException as PostgresException;

        return pgException is
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: Constraints.OpenAgentRunIndexName,
        };
    }
}
