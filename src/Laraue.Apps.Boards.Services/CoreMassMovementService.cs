using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.Exceptions.Web;
using LinqToDB;
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
    
    /// <summary>
    /// Move issue to new status.
    /// </summary>
    Task MoveIssue(
        Guid userId,
        long issueId,
        long statusId,
        CancellationToken ct);
}

public class CoreMovementService(
    DatabaseContext context,
    ICoreIssuesService issuesService,
    ISpaceCounterService spaceCounterService)
    : ICoreMovementService
{
    public async Task MoveSpace(long spaceId, long newOrganizationId, CancellationToken cancellationToken)
    {
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

        await context.Spaces
            .Where(x => x.Id == spaceId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.OrganizationId, newOrganizationId),
                cancellationToken);
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

        await UpdateIssueNumbers(affectedIssueNumbers, newSpaceId, cancellationToken);
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

        await UpdateIssueNumbers(affectedIssueNumbers, newSpaceId, cancellationToken);
    }
    
    public async Task MoveIssue(
        Guid userId,
        long issueId,
        long statusId,
        CancellationToken ct)
    {
        context.Database.EnsureTransactionStarted();

        var oldSpaceId = await context.Issues
            .Where(i => i.Id == issueId)
            .Select(i => i.Status!.Epic!.SpaceId)
            .FirstOrThrowNotFoundEFAsync($"Issue: {issueId} is not found", ct);
        
        var newSpaceId = await context.Statuses
            .Where(i => i.Id == statusId)
            .Select(i => i.Epic!.SpaceId)
            .FirstOrThrowNotFoundEFAsync($"Status: {statusId} is not found", ct);
        
        await issuesService.Update(
            issueId,
            userId,
            update => update.SetProperty(x => x.StatusId, statusId),
            newFiles: [],
            deleteAttachmentIds: [],
            ct);

        if (oldSpaceId == newSpaceId)
            return;
        
        var affectedIssueNumbers = context.IssueNumbers
            .Where(i => i.IssueId == issueId);

        await UpdateIssueNumbers(affectedIssueNumbers, newSpaceId, ct);
    }

    private async Task UpdateIssueNumbers(
        IQueryable<IssueNumber> issueNumbersQuery,
        long newSpaceId,
        CancellationToken cancellationToken)
    {
        var affectedIssueNumbers = issueNumbersQuery
            .Select(number => new
            {
                number.IssueId,
                Number = Sql.Ext.RowNumber().Over().OrderBy(number.IssueId).ToValue(),
            })
            .AsCte();

        var issuesToUpdateQueryCount = await issueNumbersQuery
            .CountAsyncEF(cancellationToken);
        
        var nextNumber = await spaceCounterService.GetNextNumber(
            newSpaceId, issuesToUpdateQueryCount, cancellationToken);
        
        await context.IssueNumbers
            .Join(affectedIssueNumbers, number => number.IssueId, n => n.IssueId, (number, n) => new { Number = number, n })
            .Set(x => x.Number.Number, x => nextNumber + x.n.Number - 1)
            .Set(x => x.Number.SpaceId, newSpaceId)
            .UpdateAsync(cancellationToken);
    }
}