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
    /// Serializes the issue link changes of an organization until the transaction ends, so the checks a
    /// change makes ("one link per pair", no Blocks cycle) can't race with another change.
    /// </summary>
    public static Task LockOrganizationIssueLinks(
        this DatabaseFacade facade,
        long organizationId,
        CancellationToken cancellationToken = default)
    {
        return facade.PgAdvisoryXactLock($"issue-links-{organizationId}", cancellationToken);
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

    /// <summary>
    /// Locks the given epics (<c>SELECT ... FOR UPDATE</c>) until the transaction ends, in id order like
    /// <see cref="LockUsers"/>.
    /// </summary>
    public static Task LockEpics(
        this DatabaseContext context,
        long[] epicIds,
        CancellationToken cancellationToken = default)
    {
        context.Database.EnsureTransactionStarted();

        return context.Epics
            .ToLinqToDB()
            .Where(x => epicIds.Contains(x.Id))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .QueryHint(PostgreSQLHints.ForUpdate)
            .ToListAsyncLinqToDB(cancellationToken);
    }
}
