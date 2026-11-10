using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using LinqToDB.EntityFrameworkCore;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// The Telegram side of <see cref="UserLastSeenTests"/>: any update records its sender as seen, also one the bot
/// refuses.
/// </summary>
public class UserLastSeenTelegramTests : TelegramIntegrationTest
{
    [Fact]
    public async Task AnyUpdate_ShouldSetLastSeenAt_WhenSenderIsAUser()
    {
        using var host = GetTelegramTestHost();

        await host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = MemberUser,
                Id = 1,
                Text = "/link",
                Chat = GroupChat,
            }
        });

        using var scope = host.CreateScope();
        var user = await scope.GetDatabaseContext().Users
            .Where(x => x.TelegramId == MemberUser.Id)
            .SingleAsyncLinqToDB();

        Assert.NotNull(user.LastSeenAt);
    }

    [Fact]
    public async Task AnyUpdate_ShouldNotSendWelcome_WhenUserSignsUpByWritingToTheBot()
    {
        // The /start reply already explains the bot; the welcome is for web sign-ups only (BRD-354).
        using var host = GetTelegramTestHost();

        await host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = MemberUser,
                Id = 1,
                Text = "/link",
                Chat = GroupChat,
            }
        });

        using var scope = host.CreateScope();
        Assert.Empty(await scope.GetDatabaseContext().UserBotMessages.ToListAsyncLinqToDB());
    }
}
