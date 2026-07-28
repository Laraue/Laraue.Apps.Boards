using Laraue.Apps.Boards.DataAccess;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

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
    public async Task MoveSpace(long spaceId, long newOrganizationId, CancellationToken cancellationToken)
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
        
        var lastIssueInNewOrganization = await context.Issues
            .Where(x => x.Status!.Epic!.Space!.OrganizationId == newOrganizationId)
            .OrderByDescending(x => x.LexoRank)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(cancellationToken);

        await context.Spaces
            .Where(x => x.Id == spaceId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.OrganizationId, newOrganizationId),
                cancellationToken);

        // When issues have already been introduced in the organization, moved issues should append to the bottom.
        if (lastIssueInNewOrganization != null)
        {
            var issueIds = await context.Issues
                .Where(x => x.Status!.Epic!.SpaceId == spaceId)
                .Select(x => x.Id)
                .ToArrayAsyncEF(cancellationToken);
            
            await issuesService.UpdateIssuesOrder(
                issueIds,
                lastIssueInNewOrganization.Id,
                OrderTargetType.After,
                cancellationToken);
        }
    }

    public async Task MoveSpaceEpics(long spaceId, long newSpaceId, CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();
        
        var updatedCount = await context.Epics
            .Where(x => x.SpaceId == spaceId)
            .Where(x => x.IsDefault == false)
            .ExecuteUpdateAsync(u => u
                .SetProperty(epic => epic.SpaceId, newSpaceId),
                cancellationToken);
        
        if (updatedCount == 0)
            return;

        var affectedIssueNumbers = context.IssueNumbers
            .Where(i => i.SpaceId == spaceId);

        await issueNumbersService.UpdateIssueNumbers(affectedIssueNumbers, newSpaceId, cancellationToken);
    }

    public async Task MoveEpic(long epicId, long newSpaceId, CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

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
    }
}