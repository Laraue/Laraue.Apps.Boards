using System.Runtime.CompilerServices;
using Laraue.Apps.Boards.DataAccess;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

public interface IIssueMonthlyCountService
{
    /// <summary>
    /// Atomically increments (creating the row if it doesn't exist yet) the issue count for
    /// <paramref name="organizationId"/>'s <paramref name="year"/>/<paramref name="month"/> bucket
    /// and returns the new total - same upsert shape as <see cref="ISpaceCounterService"/>, so
    /// this needs no separate "start of month" reset step: a new month is just a new row.
    /// </summary>
    Task<int> IncrementAndGetCount(long organizationId, int year, int month, CancellationToken cancellationToken);

    /// <summary>
    /// The current count for that bucket, or 0 if no issue has been created in it yet.
    /// </summary>
    Task<int> GetCount(long organizationId, int year, int month, CancellationToken cancellationToken);
}

public class IssueMonthlyCountService(DatabaseContext context) : IIssueMonthlyCountService
{
    private const string IncrementSqlQuery = @"
        INSERT INTO issue_monthly_counts (organization_id, year, month, count)
        VALUES ({0}, {1}, {2}, 1)
        ON CONFLICT (organization_id, year, month) DO UPDATE
        SET count = issue_monthly_counts.count + 1
        RETURNING count";

    public async Task<int> IncrementAndGetCount(long organizationId, int year, int month, CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var query = FormattableStringFactory.Create(IncrementSqlQuery, organizationId, year, month);
        var result = await context.Database
            .SqlQuery<int>(query)
            .ToListAsyncEF(cancellationToken);

        return result.First();
    }

    public Task<int> GetCount(long organizationId, int year, int month, CancellationToken cancellationToken)
    {
        return context.IssueMonthlyCounts
            .Where(x => x.OrganizationId == organizationId && x.Year == year && x.Month == month)
            .Select(x => x.Count)
            .FirstOrDefaultAsyncEF(cancellationToken);
    }
}
