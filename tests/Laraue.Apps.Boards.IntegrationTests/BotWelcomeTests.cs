using System.Security.Cryptography;
using System.Text;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// The bot's welcome after a Telegram sign-up on the web (BRD-354). The bot client is the test host's mock, shared
/// by the tests of this class, so each test uses its own Telegram id and checks only the requests to it.
/// </summary>
[Collection("IntegrationTest")]
public class BotWelcomeTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task SignIn_ShouldSendWelcomeOnce_WhenUserSignsUpWithTelegramOnTheWeb()
    {
        using var testScope = host.CreateTestScope();
        const long telegramId = 901;

        await AuthenticateAsync(telegramId);
        await AuthenticateAsync(telegramId);

        BotClientMock.Verify(
            x => x.SendRequest(
                It.Is<SendMessageRequest>(r => r.ChatId.Identifier == telegramId && r.Text.Contains("becomes a card")),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var message = await GetWelcome(testScope, telegramId);
        Assert.Equal(BotMessageStatus.Sent, message.Status);
        Assert.Null(message.Error);
    }

    [Fact]
    public async Task SignIn_ShouldRecordFailedWelcomeAndStillSignIn_WhenBotCannotWriteToUser()
    {
        using var testScope = host.CreateTestScope();
        const long telegramId = 902;
        BotClientMock
            .Setup(x => x.SendRequest(
                It.Is<SendMessageRequest>(r => r.ChatId.Identifier == telegramId),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException("Forbidden: bot can't initiate conversation with a user", 403));

        var token = await AuthenticateAsync(telegramId);

        Assert.False(string.IsNullOrEmpty(token));
        var message = await GetWelcome(testScope, telegramId);
        Assert.Equal(BotMessageStatus.Failed, message.Status);
        Assert.Contains("can't initiate conversation", message.Error);
    }

    private Mock<ITelegramBotClient> BotClientMock => Mock.Get(host.Services.GetRequiredService<ITelegramBotClient>());

    private static Task<UserBotMessage> GetWelcome(WebApiTestHostScope testScope, long telegramId)
    {
        return testScope.Database.UserBotMessages
            .Where(x => x.User!.TelegramId == telegramId && x.Kind == BotMessageKind.Welcome)
            .SingleAsync();
    }

    private Task<string?> AuthenticateAsync(long telegramId)
    {
        return host.Controller<TelegramAuthController>()
            .Execute(x => x.Authenticate(SignedWidgetData(telegramId), default));
    }

    /// <summary>
    /// Login widget data signed the way Telegram does, see <see cref="TelegramWidgetAuthTests"/>.
    /// </summary>
    private TelegramWidgetAuthRequest SignedWidgetData(long telegramId)
    {
        var botToken = host.Services.GetRequiredService<IConfiguration>()["Telegram:Token"]!;
        var authDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dataCheckString = $"auth_date={authDate}\nfirst_name=Ada\nid={telegramId}";
        var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));
        var hash = Convert.ToHexString(HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))).ToLower();

        return new TelegramWidgetAuthRequest
        {
            Id = telegramId,
            FirstName = "Ada",
            AuthDate = authDate,
            Hash = hash,
        };
    }
}
