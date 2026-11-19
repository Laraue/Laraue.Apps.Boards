using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// <see cref="DatabaseExtensions.TryClaimBotMessage"/>: the welcome and the tips claim their row before sending, so
/// each kind reaches a user once even when two requests race.
/// </summary>
public class BotMessageClaimTests : TelegramIntegrationTest
{
    [Fact]
    public async Task TryClaimBotMessage_ShouldClaimOnce_WhenCalledTwice()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var db = host.CreateScope().GetDatabaseContext();

        Assert.True(await db.TryClaimBotMessage(userId, BotMessageKind.TipReactions, DateTime.UtcNow, CancellationToken.None));
        Assert.False(await db.TryClaimBotMessage(userId, BotMessageKind.TipReactions, DateTime.UtcNow, CancellationToken.None));

        // Another kind is a separate message.
        Assert.True(await db.TryClaimBotMessage(userId, BotMessageKind.TipSaveOnlyViaSave, DateTime.UtcNow, CancellationToken.None));

        var rows = await db.UserBotMessages.AsNoTracking().ToListAsyncLinqToDB();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, x => Assert.Equal(BotMessageStatus.Sent, x.Status));
    }

    [Fact]
    public async Task TryClaimBotMessage_ShouldLetOneOfConcurrentClaimsWin_WithoutError()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();

        // Each claim on its own connection, as two requests would be: the loser gets false, not a duplicate-key error.
        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            using var scope = host.CreateScope();
            return await scope.GetDatabaseContext()
                .TryClaimBotMessage(userId, BotMessageKind.Welcome, DateTime.UtcNow, CancellationToken.None);
        })));

        Assert.Single(claims, claimed => claimed);
        var db = host.CreateScope().GetDatabaseContext();
        Assert.Single(await db.UserBotMessages.AsNoTracking().ToListAsyncLinqToDB());
    }
}
