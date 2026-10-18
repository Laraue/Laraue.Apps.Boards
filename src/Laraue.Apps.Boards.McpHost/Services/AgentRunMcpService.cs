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
    /// runs in. <paramref name="state"/> is an <see cref="AgentRunState"/> name. <paramref name="assigneeIds"/> keeps
    /// runs of issues assigned to one of these users - a runner's choice of whose issues it takes (its owner's only,
    /// a few people's, or anyone's when omitted); claiming doesn't check the assignee. <paramref name="excludeBlocked"/>
    /// leaves out runs of blocked issues - what a runner polls with, since it can't claim them.
    /// </summary>
    Task<AgentRunListPage> ListAgentRuns(
        OrganizationAuthData authData,
        string? spaceKey,
        string? state,
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

    /// <summary><paramref name="state"/> is "Done" or "Failed".</summary>
    Task<bool> FinishAgentRun(
        OrganizationAuthData authData,
        long runId,
        string state,
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

        var issueId = await GetIssueIdByIssueKey(authData.OrganizationId, key, cancellationToken);

        await EnsureCanUpdateIssue(authData, issueId, "Issue", key.ToString(), cancellationToken);

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
        string? state,
        IReadOnlyList<Guid>? assigneeIds,
        bool excludeBlocked,
        int? page,
        int? count,
        CancellationToken cancellationToken)
    {
        AgentRunState? parsedState = null;
        if (state is not null)
            parsedState = ParseState(nameof(state), state);

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

            if (parsedState is not null)
                runs = runs.Where(r => r.State == parsedState);

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
        await EnsureCanUpdateRun(authData, runId, cancellationToken);

        var readableSpaceIds = await GetReadableSpaceIds(authData, cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var claimed = await coreAgentRunsService.Claim(runId, readableSpaceIds, authData.ToActor(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return claimed;
    }

    public async Task<bool> FinishAgentRun(
        OrganizationAuthData authData,
        long runId,
        string state,
        string? reason,
        CancellationToken cancellationToken)
    {
        var parsedState = ParseState(nameof(state), state);
        if (parsedState is not (AgentRunState.Done or AgentRunState.Failed))
            throw new BadRequestException(nameof(state), ErrorMessages.AgentRunFinishStateInvalid);

        await EnsureCanUpdateRun(authData, runId, cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var finished = await coreAgentRunsService.Finish(runId, parsedState, reason, authData.ToActor(), cancellationToken);
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

    private async Task EnsureCanUpdateRun(
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

        await EnsureCanUpdateIssue(authData, issueId, "Agent run", runId.ToString(), cancellationToken);
    }

    // 404 when the issue isn't readable, 403 when it's readable but the caller can't change it - see AGENTS.md
    // "Choosing 404 vs 403".
    private async Task EnsureCanUpdateIssue(
        OrganizationAuthData authData,
        long issueId,
        string entityName,
        string entityId,
        CancellationToken cancellationToken)
    {
        var accessLevels = await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, entityName, entityId))
            .EnsureOrThrowNotFound(a => a.CanRead, string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, entityName, entityId));

        if (!accessLevels.CanUpdateIssue)
            throw new ForbiddenException(string.Format(ErrorMessages.EntityActionForbidden, entityName, entityId, "update"));
    }

    private static AgentRunState ParseState(string argumentName, string state)
    {
        if (!Enum.TryParse<AgentRunState>(state, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed)
            || int.TryParse(state, out _))
        {
            throw new BadRequestException(
                argumentName,
                string.Format(ErrorMessages.EntityNotFound, "Agent run state", state));
        }

        return parsed;
    }

    private Task<long> GetIssueIdByIssueKey(long organizationId, IssueKey issueKey, CancellationToken cancellationToken)
    {
        return context.IssueNumbers
            .Where(x => x.Number == issueKey.Number)
            .Where(x => x.Space!.Key == issueKey.SpaceKey)
            .Where(x => x.Space!.OrganizationId == organizationId)
            .Where(x => x.Issue!.DeletedAt == null)
            .Select(x => x.IssueId)
            .FirstOrThrowNotFoundEFAsync(string.Format(ErrorMessages.IssueNotFoundInOrganization, issueKey), cancellationToken);
    }
}
