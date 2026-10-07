using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

public interface IIssuePeriodCountService
{
    /// <summary>
    /// Atomically adds one to the issue count of <paramref name="organizationId"/>'s period that started at
    /// <paramref name="periodStartedAt"/>, creating the row if it doesn't exist yet - same upsert shape as
    /// <see cref="ISpaceCounterService"/>, so this needs no separate "start of period" reset step: a new
    /// period is just a new row. Needs a transaction started by the caller.
    /// </summary>
    Task Increment(long organizationId, DateTime periodStartedAt, CancellationToken cancellationToken);

    /// <summary>
    /// The current count for that period, or 0 if no issue has been created in it yet.
    /// </summary>
    Task<int> GetCount(long organizationId, DateTime periodStartedAt, CancellationToken cancellationToken);
}

public class IssuePeriodCountService(DatabaseContext context) : IIssuePeriodCountService
{
    public async Task Increment(long organizationId, DateTime periodStartedAt, CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        // One INSERT ... ON CONFLICT DO UPDATE statement, so two issues created at once cannot lose a count.
        await context.IssuePeriodCounts
            .ToLinqToDBTable()
            .InsertOrUpdateAsync(
                () => new IssuePeriodCount
                {
                    OrganizationId = organizationId,
                    PeriodStartedAt = periodStartedAt,
                    Count = 1,
                },
                existing => new IssuePeriodCount { Count = existing.Count + 1 },
                () => new IssuePeriodCount
                {
                    OrganizationId = organizationId,
                    PeriodStartedAt = periodStartedAt,
                },
                cancellationToken);
    }

    public Task<int> GetCount(long organizationId, DateTime periodStartedAt, CancellationToken cancellationToken)
    {
        return context.IssuePeriodCounts
            .Where(x => x.OrganizationId == organizationId && x.PeriodStartedAt == periodStartedAt)
            .Select(x => x.Count)
            .FirstOrDefaultAsyncEF(cancellationToken);
    }
}
