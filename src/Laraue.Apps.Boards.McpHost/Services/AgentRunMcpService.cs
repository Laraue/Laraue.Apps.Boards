using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.McpHost.Resources;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Links;
using Laraue.Apps.Boards.Services.Members;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.McpHost.Services;

/// <summary>
/// Backs <see cref="Laraue.Apps.Boards.McpHost.Tools.AgentRunTools"/> - handing issues to an agent and the runner's
/// side of a run (BRD-305). Permission checks are the issue's own: reading runs needs <c>CanRead</c>, every change
/// needs <c>CanUpdateIssue</c>. The state changes themselves are <see cref="ICoreAgentRunsService"/>'s.
/// </summary>
public interface IAgentRunMcpService
{
    /// <summary>Hands the issue off - creates a Ready run. Fails when the issue already has an open run.</summary>
    Task<AgentRunSummary> CreateAgentRun(
        OrganizationAuthData authData,
        string issueKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// One page of runs of the issues the caller can read, oldest first - the order a runner should take Ready
    /// runs in. <paramref name="assigneeIds"/> keeps
    /// runs of issues assigned to one of these users - a runner's choice of whose issues it takes (its owner's only,
    /// a few people's, or anyone's when omitted); claiming doesn't check the assignee. <paramref name="excludeBlocked"/>
    /// leaves out runs of blocked issues - what a runner polls with, since it can't claim them.
    /// </summary>
    Task<AgentRunListPage> ListAgentRuns(
        OrganizationAuthData authData,
        string? spaceKey,
        AgentRunState? state,
        IReadOnlyList<Guid>? assigneeIds,
        bool excludeBlocked,
        int? page,
        int? count,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ready -> Working. False when the run isn't Ready any more, or its issue is blocked (see
    /// <see cref="AgentRunSummary.IsBlocked"/>).
    /// </summary>
    Task<bool> ClaimAgentRun(
        OrganizationAuthData authData,
        long runId,
        CancellationToken cancellationToken);

    Task<bool> FinishAgentRun(
        OrganizationAuthData authData,
        long runId,
        AgentRunFinishState state,
        string? reason,
        CancellationToken cancellationToken);

    Task<bool> CancelAgentRun(
        OrganizationAuthData authData,
        long runId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The issue's latest run, or null when it was never handed off. No permission check - for
    /// <see cref="IIssueMcpService.GetIssue"/>, which has already checked that the issue is readable.
    /// </summary>
    Task<AgentRunSummary?> GetLatestRun(
        OrganizationAuthData authData,
        long issueId,
        CancellationToken cancellationToken);
}

/// <summary>
/// An agent run (one hand-off of an issue). <see cref="State"/> is Ready, Working, Done, Failed or Cancelled;
/// <see cref="ClaimedByApiKey"/> names the runner's API key once it claimed the run. <see cref="IsBlocked"/>: an issue
/// the caller can read blocks this run's issue and isn't Completed yet - a blocked Ready run can't be claimed.
/// </summary>
public sealed record AgentRunSummary(
    long Id,
    string IssueKey,
    string IssueTitle,
    string IssueUrl,
    string State,
    DateTime CreatedAt,
    string CreatedBy,
    DateTime? ClaimedAt,
    string? ClaimedByApiKey,
    DateTime? FinishedAt,
    string? FailureReason,
    bool IsBlocked);

/// <summary>How a runner ends a run: the work is done, or the agent gave up.</summary>
public enum AgentRunFinishState
{
    Done,
    Failed,
}

public sealed record AgentRunListPage(IReadOnlyList<AgentRunSummary> Runs, long Page, bool HasNextPage);

public class AgentRunMcpService(
    DatabaseContext context,
    IAccessService accessService,
    ICoreAgentRunsService coreAgentRunsService,
    IIssueUrlBuilder issueUrlBuilder,
    IIssueLinksReader issueLinksReader,
    IMemberProfileReader memberProfileReader)
    : IAgentRunMcpService
{
    private const int MaxResults = 50;

    public async Task<AgentRunSummary> CreateAgentRun(
        OrganizationAuthData authData,
        string issueKey,
        CancellationToken cancellationToken)
    {
        var key = new IssueKey(issueKey);

        var issue = await accessService.GetIssueAccessByKey(authData, key, includeDeleted: false, cancellationToken);
        EnsureCanUpdateIssue(issue?.AccessLevels, "Issue", key.ToString());
        var issueId = issue!.IssueId;

        await EnsureIsAssignee(
            issueId,
            authData.UserId,
            string.Format(ErrorMessages.AgentRunHandOffOnlyAssignee, key),
            cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var runId = await coreAgentRunsService.Create(issueId, authData.ToActor(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var runs = await MapRuns(
            authData,
            context.IssueAgentRuns
                .Where(r => r.Id == runId),
            cancellationToken);

        return runs.Single();
    }

    public async Task<AgentRunListPage> ListAgentRuns(
        OrganizationAuthData authData,
        string? spaceKey,
        AgentRunState? state,
        IReadOnlyList<Guid>? assigneeIds,
        bool excludeBlocked,
        int? page,
        int? count,
        CancellationToken cancellationToken)
    {
        var pagination = new PaginationData
        {
            Page = page ?? 0,
            PerPage = Math.Clamp(count ?? MaxResults, 1, MaxResults),
        };

        return await accessService.GetAvailableIssues(authData, async readableIssues =>
        {
            var issues = readableIssues;

            if (!string.IsNullOrWhiteSpace(spaceKey))
                issues = issues.Where(i => i.Status!.Epic!.Space!.Key == spaceKey);

            if (assigneeIds is { Count: > 0 })
                issues = issues.Where(i => assigneeIds.Contains(i.AssigneeId));

            // Blocked = an issue the caller can read blocks it and isn't Completed - the rule of list_issues'
            // excludeBlocked, the web app's "blocked" mark, and the claim.
            if (excludeBlocked)
            {
                issues = issues.Where(i => !context.IssueLinks.Any(l =>
                    l.Type == IssueLinkType.Blocks
                    && l.TargetIssueId == i.Id
                    && readableIssues.Any(b => b.Id == l.SourceIssueId && b.Status!.Category != StatusCategory.Completed)));
            }

            var runs = context.IssueAgentRuns
                .Where(r => issues
                    .Select(i => i.Id)
                    .Contains(r.IssueId));

            if (state is not null)
                runs = runs.Where(r => r.State == state);

            var runIds = await runs
                .OrderBy(r => r.Id)
                .Select(r => new { r.Id })
                .ShortPaginateEFAsync(pagination, cancellationToken);

            var pageIds = runIds.Data
                .Select(x => x.Id)
                .ToArray();

            var summaries = await MapRuns(
                authData,
                context.IssueAgentRuns
                    .Where(r => pageIds.Contains(r.Id)),
                cancellationToken);

            return new AgentRunListPage(summaries, runIds.Page, runIds.HasNextPage);
        }, cancellationToken);
    }

    public async Task<bool> ClaimAgentRun(
        OrganizationAuthData authData,
        long runId,
        CancellationToken cancellationToken)
    {
        var issueId = await EnsureCanUpdateRun(authData, runId, cancellationToken);
        await EnsureIsAssignee(
            issueId,
            authData.UserId,
            string.Format(ErrorMessages.AgentRunClaimOnlyAssignee, runId),
            cancellationToken);

        // A blocked issue's run waits - the same rule as isBlocked and excludeBlocked: a blocker the caller can read
        // that isn't Completed. Checked before the claim, not inside its update: a blocker linked in between counts
        // as one linked just after the claim, which doesn't stop the agent either.
        var blockedIssueIds = await issueLinksReader.GetBlockedIssueIds(
            [issueId],
            await GetReadableSpaceIds(authData, cancellationToken),
            cancellationToken);
        if (blockedIssueIds.Count > 0)
            return false;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var claimed = await coreAgentRunsService.Claim(runId, authData.ToActor(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return claimed;
    }

    public async Task<bool> FinishAgentRun(
        OrganizationAuthData authData,
        long runId,
        AgentRunFinishState state,
        string? reason,
        CancellationToken cancellationToken)
    {
        var finishState = state switch
        {
            AgentRunFinishState.Done => AgentRunState.Done,
            AgentRunFinishState.Failed => AgentRunState.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

        await EnsureCanUpdateRun(authData, runId, cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var finished = await coreAgentRunsService.Finish(runId, finishState, reason, authData.ToActor(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return finished;
    }

    public async Task<bool> CancelAgentRun(
        OrganizationAuthData authData,
        long runId,
        CancellationToken cancellationToken)
    {
        await EnsureCanUpdateRun(authData, runId, cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var cancelled = await coreAgentRunsService.Cancel(runId, authData.ToActor(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return cancelled;
    }

    public async Task<AgentRunSummary?> GetLatestRun(
        OrganizationAuthData authData,
        long issueId,
        CancellationToken cancellationToken)
    {
        var runs = await MapRuns(
            authData,
            context.IssueAgentRuns
                .Where(r => r.IssueId == issueId)
                .OrderByDescending(r => r.Id)
                .Take(1),
            cancellationToken);

        return runs.SingleOrDefault();
    }

    private async Task<List<AgentRunSummary>> MapRuns(
        OrganizationAuthData authData,
        IQueryable<IssueAgentRun> runs,
        CancellationToken cancellationToken)
    {
        var data = await runs
            .Select(r => new
            {
                r.Id,
                r.IssueId,
                SpaceKey = r.Issue!.IssueNumber!.Space!.Key,
                r.Issue.IssueNumber.Number,
                r.Issue.Title,
                r.State,
                r.CreatedAt,
                CreatedBy = new UserDetails { UserId = r.CreatedByUserId },
                r.ClaimedAt,
                ClaimedByApiKey = r.ClaimedByApiKey!.Name,
                r.FinishedAt,
                r.FailureReason,
            })
            .ToListAsync(cancellationToken);

        await memberProfileReader.EnrichUsers(
            authData.OrganizationId,
            data
                .Select(x => x.CreatedBy),
            cancellationToken);

        var blockedIssueIds = await issueLinksReader.GetBlockedIssueIds(
            data
                .Select(x => x.IssueId)
                .Distinct()
                .ToArray(),
            await GetReadableSpaceIds(authData, cancellationToken),
            cancellationToken);

        var organization = await context.ActiveOrganizations()
            .Where(o => o.Id == authData.OrganizationId)
            .Select(o => new { o.Slug, o.SlugPostfix })
            .SingleAsync(cancellationToken);

        return data
            .OrderBy(x => x.Id)
            .Select(x =>
            {
                var key = new IssueKey(x.SpaceKey, x.Number);

                return new AgentRunSummary(
                    x.Id,
                    key.ToString(),
                    x.Title,
                    issueUrlBuilder.Build(organization.Slug, organization.SlugPostfix, key),
                    x.State.ToString(),
                    x.CreatedAt,
                    x.CreatedBy.DisplayName,
                    x.ClaimedAt,
                    x.ClaimedByApiKey,
                    x.FinishedAt,
                    x.FailureReason,
                    blockedIssueIds.Contains(x.IssueId));
            })
            .ToList();
    }

    private Task<long[]> GetReadableSpaceIds(OrganizationAuthData authData, CancellationToken cancellationToken)
    {
        return accessService.GetAvailableSpaces(
            authData,
            query => query
                .Select(x => x.Id)
                .ToArrayAsyncEF(cancellationToken),
            includeDeleted: false,
            cancellationToken);
    }

    // Returns the run's issue id.
    private async Task<long> EnsureCanUpdateRun(
        OrganizationAuthData authData,
        long runId,
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

        return issueId;
    }

    // A run belongs to the issue's assignee: only they hand it off, only their runner claims it. The assignee can't
    // change while the run is open (the issue is locked), so the run can't move to someone else's machine.
    private async Task EnsureIsAssignee(
        long issueId,
        Guid userId,
        string message,
        CancellationToken cancellationToken)
    {
        var assigneeId = await context.ActiveIssues()
            .Where(i => i.Id == issueId)
            .Select(i => i.AssigneeId)
            .FirstAsync(cancellationToken);

        if (assigneeId != userId)
            throw new ForbiddenException(message);
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
