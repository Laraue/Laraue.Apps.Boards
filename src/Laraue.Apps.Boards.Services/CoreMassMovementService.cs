using Laraue.Apps.Boards.DataAccess;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Service for issues movement. After each movement renumbering and reordering triggered.
/// </summary>
public interface ICoreMovementService
{
    /// <summary>
    /// Move space to the new organization.
    /// </summary>
    Task MoveSpace(
        long spaceId,
        long newOrganizationId,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Move space epics to the new space of any organization.
    /// </summary>
    Task MoveSpaceEpics(
        long spaceId,
        long newSpaceId,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Move epic to the new space of any organization.
    /// </summary>
    Task MoveEpic(
        long epicId,
        long newSpaceId,
        CancellationToken cancellationToken);
}

public class CoreMovementService(
    DatabaseContext context,
    IIssueNumbersService issueNumbersService,
    ICoreIssuesService issuesService)
    : ICoreMovementService
{
    public async Task MoveSpace(
        long spaceId,
        long newOrganizationId,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();
        
        var sourceData = await context.Spaces
            .Where(x => x.Id == spaceId)
            .Select(x => new { x.IsDefault, x.Key })
            .FirstOrThrowNotFoundEFAsync($"Space: {spaceId} is not found", cancellationToken);
        
        if (sourceData.IsDefault)
            throw new ForbiddenException("Default space cannot be moved.");
        
        var suchSpaceKeyExists = await context.Spaces
            .Where(x => x.OrganizationId == newOrganizationId)
            .Where(x => x.Key == sourceData.Key)
            .AnyAsyncEF(cancellationToken);
        
        if (suchSpaceKeyExists)
            throw new BadRequestException(
                nameof(newOrganizationId),
                $"Space key {sourceData.Key} already exists in target organization.");
        
        var lastIssueInNewOrganization = await GetLastOrganizationIssue(newOrganizationId, cancellationToken);

        await context.Spaces
            .Where(x => x.Id == spaceId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.OrganizationId, newOrganizationId),
                cancellationToken);

        if (lastIssueInNewOrganization.HasValue)
        {
            var issueIds = await context.Issues
                .Where(x => x.Status!.Epic!.SpaceId == spaceId)
                .Select(x => x.Id)
                .ToArrayAsyncEF(cancellationToken);
            
            await issuesService.UpdateIssuesOrder(
                issueIds,
                lastIssueInNewOrganization.Value,
                OrderTargetType.After,
                cancellationToken);
        }
    }

    public async Task MoveSpaceEpics(
        long spaceId,
        long newSpaceId,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();
        
        var organizationIdBySpaceId = await context.Spaces
            .Where(x => x.Id == spaceId || x.Id == newSpaceId)
            .ToDictionaryAsyncEF(x => x.Id, x => x.OrganizationId, cancellationToken);

        var newOrganizationId = organizationIdBySpaceId[newSpaceId];
        var organizationWillChanged = organizationIdBySpaceId[spaceId] != newOrganizationId;
        
        long? lastIssueInNewOrganization = null;
        if (organizationWillChanged)
            lastIssueInNewOrganization = await GetLastOrganizationIssue(newOrganizationId, cancellationToken);
        
        var epicsIdsToUpdate = await context.Epics
            .Where(x => x.SpaceId == spaceId)
            .Where(x => x.IsDefault == false)
            .Select(x => x.Id)
            .ToArrayAsyncEF(cancellationToken);

        var updatedCount = await context.Epics
            .Where(x => ((IEnumerable<long>)epicsIdsToUpdate).Contains(x.Id))
            .ExecuteUpdateAsync(u => u
                .SetProperty(epic => epic.SpaceId, newSpaceId),
                cancellationToken);
        
        if (updatedCount == 0)
            return;

        var affectedIssueNumbers = context.IssueNumbers
            .Where(i => i.SpaceId == spaceId);

        await issueNumbersService.UpdateIssueNumbers(affectedIssueNumbers, newSpaceId, cancellationToken);
        
        if (lastIssueInNewOrganization.HasValue)
        {
            var issueIds = await context.Issues
                .Where(x => ((IEnumerable<long>)epicsIdsToUpdate).Contains(x.Status!.EpicId))
                .Select(x => x.Id)
                .ToArrayAsyncEF(cancellationToken);
            
            await issuesService.UpdateIssuesOrder(
                issueIds,
                lastIssueInNewOrganization.Value,
                OrderTargetType.After,
                cancellationToken);
        }  
    }

    public async Task MoveEpic(
        long epicId,
        long newSpaceId,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var oldOrganizationId = await context.Epics
            .Where(x => x.Id == epicId)
            .Select(x => x.Space!.OrganizationId)
            .FirstAsyncEF(cancellationToken);
        
        var newOrganizationId = await context.Spaces
            .Where(x => x.Id == newSpaceId)
            .Select(x => x.OrganizationId)
            .FirstAsyncEF(cancellationToken);

        var organizationWillChanged = oldOrganizationId != newOrganizationId;
        long? lastIssueInNewOrganization = null;
        if (organizationWillChanged)
            lastIssueInNewOrganization = await GetLastOrganizationIssue(newOrganizationId, cancellationToken);

        var sourceData = await context.Epics
            .Where(x => x.Id == epicId)
            .Select(x => new { x.IsDefault })
            .FirstOrThrowNotFoundEFAsync($"Epic: {epicId} is not found", cancellationToken);
        
        if (sourceData.IsDefault)
            throw new ForbiddenException("Default epic cannot be moved.");
        
        var updatedCount = await context.Epics
            .Where(x => x.Id == epicId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(epic => epic.SpaceId, newSpaceId),
                cancellationToken);
        
        if (updatedCount == 0)
            return;
        
        var affectedIssueNumbers = context.IssueNumbers
            .Where(i => i.Issue!.Status!.EpicId == epicId);

        await issueNumbersService.UpdateIssueNumbers(affectedIssueNumbers, newSpaceId, cancellationToken);
        
        if (lastIssueInNewOrganization.HasValue)
        {
            var issueIds = await context.Issues
                .Where(x => x.Status!.EpicId == epicId)
                .Select(x => x.Id)
                .ToArrayAsyncEF(cancellationToken);
            
            await issuesService.UpdateIssuesOrder(
                issueIds,
                lastIssueInNewOrganization.Value,
                OrderTargetType.After,
                cancellationToken);
        }
    }

    private async Task<long?> GetLastOrganizationIssue(long organizationId, CancellationToken cancellationToken)
    {
        var lastIssueInNewOrganization = await context.Issues
            .Where(x => x.Status!.Epic!.Space!.OrganizationId == organizationId)
            .OrderByDescending(x => x.LexoRank)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(cancellationToken);
        
        return lastIssueInNewOrganization?.Id;
    }
}