using System.Runtime.CompilerServices;
using Laraue.Apps.Boards.DataAccess;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

public interface IIssuePeriodCountService
{
    /// <summary>
    /// Atomically increments (creating the row if it doesn't exist yet) the issue count for
    /// <paramref name="organizationId"/>'s period that started at <paramref name="periodStartedAt"/>
    /// and returns the new total - same upsert shape as <see cref="ISpaceCounterService"/>, so this
    /// needs no separate "start of period" reset step: a new period is just a new row.
    /// </summary>
    Task<int> IncrementAndGetCount(long organizationId, DateTime periodStartedAt, CancellationToken cancellationToken);

    /// <summary>
    /// The current count for that period, or 0 if no issue has been created in it yet.
    /// </summary>
    Task<int> GetCount(long organizationId, DateTime periodStartedAt, CancellationToken cancellationToken);
}

public class IssuePeriodCountService(DatabaseContext context) : IIssuePeriodCountService
{
    private const string IncrementSqlQuery = @"
        INSERT INTO issue_period_counts (organization_id, period_started_at, count)
        VALUES ({0}, {1}, 1)
        ON CONFLICT (organization_id, period_started_at) DO UPDATE
        SET count = issue_period_counts.count + 1
        RETURNING count";

    public async Task<int> IncrementAndGetCount(long organizationId, DateTime periodStartedAt, CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var query = FormattableStringFactory.Create(IncrementSqlQuery, organizationId, periodStartedAt);
        var result = await context.Database
            .SqlQuery<int>(query)
            .ToListAsyncEF(cancellationToken);

        return result.First();
    }

    public Task<int> GetCount(long organizationId, DateTime periodStartedAt, CancellationToken cancellationToken)
    {
        return context.IssuePeriodCounts
            .Where(x => x.OrganizationId == organizationId && x.PeriodStartedAt == periodStartedAt)
            .Select(x => x.Count)
            .FirstOrDefaultAsyncEF(cancellationToken);
    }
}
