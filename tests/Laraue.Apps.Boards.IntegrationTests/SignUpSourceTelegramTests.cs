using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using LinqToDB.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// The bot side of <see cref="SignUpSourceTests"/>: a user created by a deep link's <c>/start producthunt</c> keeps
/// <c>producthunt</c> as where they came from (BRD-295).
/// </summary>
public class SignUpSourceTelegramTests : TelegramIntegrationTest
{
    [Fact]
    public async Task StartCommand_ShouldStoreDeepLinkParameter_WhenItCreatesTheUser()
    {
        using var host = GetTelegramTestHost();

        await SendStart(host, "/start producthunt");

        Assert.Equal("producthunt", await GetSource(host));
    }

    [Fact]
    public async Task StartCommand_ShouldStoreNoSource_WhenItHasNoParameter()
    {
        using var host = GetTelegramTestHost();

        await SendStart(host, "/start");

        Assert.Null(await GetSource(host));
    }

    private static Task SendStart(AppTelegramTestHost host, string text)
    {
        return host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = DefaultUser,
                Id = 1,
                Text = text,
                Chat = new Chat { Id = DefaultUser.Id, Type = ChatType.Private },
            }
        });
    }

    private static async Task<string?> GetSource(AppTelegramTestHost host)
    {
        using var scope = host.CreateScope();
        var user = await scope.GetDatabaseContext().Users
            .Where(x => x.TelegramId == DefaultUser.Id)
            .SingleAsyncLinqToDB();

        return user.SignUpSource;
    }
}
