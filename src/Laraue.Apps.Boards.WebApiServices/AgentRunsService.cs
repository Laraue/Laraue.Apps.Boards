using System.Linq.Expressions;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
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
}

public class AgentRunsService(
    DatabaseContext context,
    IAccessService accessService,
    ICoreAgentRunsService coreAgentRunsService)
    : IAgentRunsService
{
    public async Task<AgentRunDto> Create(
        CreateAgentRunRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var key = new IssueKey(request.IssueKey);

        var issueId = await context.IssueNumbers
            .Where(x => x.Number == key.Number)
            .Where(x => x.Space!.Key == key.SpaceKey)
            .Where(x => x.Space!.OrganizationId == authData.OrganizationId)
            .Where(x => x.Issue!.DeletedAt == null)
            .Select(x => x.IssueId)
            .FirstOrThrowNotFoundEFAsync(
                string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", key),
                cancellationToken);

        await EnsureCanUpdateIssue(authData, issueId, "Issue", key.ToString(), cancellationToken);

        // Only the assignee hands an issue to their agent; see AgentRunMcpService.EnsureIsAssignee.
        var assigneeId = await context.ActiveIssues()
            .Where(i => i.Id == issueId)
            .Select(i => i.AssigneeId)
            .FirstAsync(cancellationToken);

        if (assigneeId != authData.UserId)
            throw new ForbiddenException(string.Format(ErrorMessages.AgentRunHandOffOnlyAssignee, key));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var runId = await coreAgentRunsService.Create(issueId, authData.ToActor(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await context.IssueAgentRuns
            .Where(r => r.Id == runId)
            .Select(AgentRunDto.Projection)
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

        await EnsureCanUpdateIssue(authData, issueId, "Agent run", runId.ToString(), cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var cancelled = await coreAgentRunsService.Cancel(runId, authData.ToActor(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return cancelled;
    }

    // 404 when the issue isn't readable, 403 when it's readable but can't be changed - AGENTS.md "Choosing 404 vs 403".
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

    public static readonly Expression<Func<IssueAgentRun, AgentRunDto>> Projection = r => new AgentRunDto
    {
        Id = r.Id,
        State = r.State,
        CreatedAt = r.CreatedAt,
        ClaimedAt = r.ClaimedAt,
        ClaimedByApiKeyName = r.ClaimedByApiKey!.Name,
        FinishedAt = r.FinishedAt,
        FailureReason = r.FailureReason,
    };
}
