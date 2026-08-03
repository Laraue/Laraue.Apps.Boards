using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

public interface IIssueNumbersService
{
    /// <summary>
    /// Update issue numbers, returns dictionary with new numbers.
    /// </summary>
    /// <param name="issueNumbersQuery"></param>
    /// <param name="newSpaceId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task UpdateIssueNumbers(
        IQueryable<IssueNumber> issueNumbersQuery,
        long newSpaceId,
        CancellationToken cancellationToken);
}

public class IssueNumbersService(ISpaceCounterService spaceCounterService, DatabaseContext context)
    : IIssueNumbersService
{
    public async Task UpdateIssueNumbers(
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