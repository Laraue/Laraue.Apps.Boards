using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Laraue.Apps.Boards.Services;

public static class DatabaseFacadeExtensions
{
    public static void EnsureTransactionStarted(this DatabaseFacade facade)
    {
        if (facade.CurrentTransaction == null)
            throw new InvalidOperationException("Database transaction is required.");
    }

    public static Task PgAdvisoryXactLock(this DatabaseFacade facade, string lockKey, CancellationToken cancellationToken = default)
    {
        facade.EnsureTransactionStarted();
        
        return facade.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtext({0})::bigint)",
            [lockKey],
            cancellationToken);
    }
}