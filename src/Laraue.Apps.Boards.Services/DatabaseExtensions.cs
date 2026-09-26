using System.Linq.Expressions;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using LinqToDB;
using LinqToDB.DataProvider.PostgreSQL;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Laraue.Apps.Boards.Services;

public static class DatabaseExtensions
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

    /// <summary>
    /// Locks the matching users (<c>SELECT ... FOR UPDATE</c>) until the transaction ends. Rows are locked in
    /// id order so two transactions locking overlapping sets of users can't deadlock.
    /// </summary>
    public static Task LockUsers(
        this DatabaseContext context,
        Expression<Func<User, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        context.Database.EnsureTransactionStarted();

        return context.Users
            .ToLinqToDB()
            .Where(predicate)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            // Not AsPostgreSQL().ForUpdateHint(): that wrapper loses the EF Core bridge, so it can't run async.
            .QueryHint(PostgreSQLHints.ForUpdate)
            .ToListAsyncLinqToDB(cancellationToken);
    }
}
