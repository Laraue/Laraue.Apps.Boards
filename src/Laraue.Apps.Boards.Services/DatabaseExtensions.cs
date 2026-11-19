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

    /// <summary>
    /// Claims the bot's unprompted message of <paramref name="kind"/> for the user before it's sent - a
    /// <see cref="UserBotMessage"/> row, at most one per user and kind. Insert-if-not-exists (on Postgres
    /// <c>on conflict do nothing</c>), so of two requests at once exactly one gets <c>true</c> and sends; a duplicate
    /// is no error. EF Core has no such insert, so this goes through LinqToDB.
    /// </summary>
    /// <returns>True when this call claimed the message and should send it.</returns>
    public static async Task<bool> TryClaimBotMessage(
        this DatabaseContext context,
        Guid userId,
        BotMessageKind kind,
        DateTime sentAt,
        CancellationToken cancellationToken)
    {
        var inserted = await context.UserBotMessages
            .ToLinqToDBTable()
            .InsertOrUpdateAsync(
                () => new UserBotMessage
                {
                    UserId = userId,
                    Kind = kind,
                    SentAt = sentAt,
                    Status = BotMessageStatus.Sent,
                },
                // No setters: nothing to update when the row exists - insert if not exists.
                _ => new UserBotMessage(),
                cancellationToken);

        return inserted > 0;
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
    /// Locks a recorded Telegram message (<c>SELECT ... FOR UPDATE</c>) until the transaction ends, so two requests
    /// turning it into a card run one after another and the second sees the first's card.
    /// </summary>
    public static Task LockTelegramMessage(
        this DatabaseContext context,
        long telegramMessageId,
        CancellationToken cancellationToken = default)
    {
        context.Database.EnsureTransactionStarted();

        return context.TelegramMessages
            .ToLinqToDB()
            .Where(x => x.Id == telegramMessageId)
            .Select(x => x.Id)
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
