using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Grpc.Core;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Apps.Identity.Internal.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Telegram.Bot;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Where a user came from (BRD-295), on the web sign-in paths: stored on the new Boards user and sent to Identity.
/// The bot's <c>/start</c> parameter is covered in <see cref="SignUpSourceTelegramTests"/>.
/// </summary>
[Collection("IntegrationTest")]
public class SignUpSourceTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task WidgetSignIn_ShouldStoreAndSendNormalizedSource_WhenNewUserCameFromALink()
    {
        using var testScope = host.CreateTestScope();
        const long telegramId = 1201;

        var token = await AuthenticateWithWidget(telegramId, "ProductHunt");

        Assert.False(string.IsNullOrEmpty(token));
        Assert.Equal("producthunt", await GetSource(testScope, telegramId));
        IdentityClientMock.Verify(
            x => x.CreateUserIfNotExistsAsync(
                It.Is<CreateUserIfNotExistsRequest>(r => r.TelegramId == telegramId && r.Source == "producthunt"),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WidgetSignIn_ShouldKeepFirstSource_WhenUserSignsInAgainFromAnotherLink()
    {
        using var testScope = host.CreateTestScope();
        const long telegramId = 1202;

        await AuthenticateWithWidget(telegramId, "productradar");
        await AuthenticateWithWidget(telegramId, "producthunt");

        Assert.Equal("productradar", await GetSource(testScope, telegramId));
    }

    [Fact]
    public async Task MiniAppSignIn_ShouldUseStartParam_WhenFrontendSentNoSource()
    {
        using var testScope = host.CreateTestScope();
        const long telegramId = 1203;

        await host.Controller<TelegramAuthController>()
            .Execute(x => x.Authenticate(
                new AuthenticateViaStringInitDataRequest { InitData = SignedInitData(telegramId, startParam: "channel_post") },
                default));

        Assert.Equal("channel_post", await GetSource(testScope, telegramId));
    }

    [Fact]
    public async Task MiniAppSignIn_ShouldPreferFrontendSource_WhenBothAreSent()
    {
        using var testScope = host.CreateTestScope();
        const long telegramId = 1204;

        await host.Controller<TelegramAuthController>()
            .Execute(x => x.Authenticate(
                new AuthenticateViaStringInitDataRequest
                {
                    InitData = SignedInitData(telegramId, startParam: "channel_post"),
                    SignUpSource = "producthunt",
                },
                default));

        Assert.Equal("producthunt", await GetSource(testScope, telegramId));
    }

    [Fact]
    public async Task GoogleSignIn_ShouldStoreAndSendSource_WhenNewUserCameFromALink()
    {
        using var testScope = host.CreateTestScope();
        host.GoogleIdTokenValidatorMock
            .Setup(x => x.ValidateAsync("source-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdTokenPayload("google-source-1", "ann@example.com", "Ann Lee", "Ann", "Lee"));

        await host.Controller<GoogleAuthController>()
            .Execute(x => x.Authenticate(
                new GoogleAuthRequest { IdToken = "source-token", SignUpSource = "habr" },
                default));

        var source = await testScope.Database.Users
            .Where(x => x.GoogleSubject == "google-source-1")
            .Select(x => x.SignUpSource)
            .SingleAsync();
        Assert.Equal("habr", source);
        IdentityClientMock.Verify(
            x => x.CreateUserIfNotExistsByGoogleAsync(
                It.Is<CreateUserIfNotExistsByGoogleRequest>(r => r.GoogleSubject == "google-source-1" && r.Source == "habr"),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private Mock<UserIdentityService.UserIdentityServiceClient> IdentityClientMock =>
        Mock.Get(host.Services.GetRequiredService<UserIdentityService.UserIdentityServiceClient>());

    private static Task<string?> GetSource(WebApiTestHostScope testScope, long telegramId)
    {
        return testScope.Database.Users
            .Where(x => x.TelegramId == telegramId)
            .Select(x => x.SignUpSource)
            .SingleAsync();
    }

    private string BotToken => host.Services.GetRequiredService<IConfiguration>()["Telegram:Token"]!;

    /// <summary>
    /// Login widget data signed the way Telegram does (see <see cref="TelegramWidgetAuthTests"/>); the source is the
    /// frontend's own field and isn't signed.
    /// </summary>
    private Task<string?> AuthenticateWithWidget(long telegramId, string source)
    {
        var authDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dataCheckString = $"auth_date={authDate}\nfirst_name=Ada\nid={telegramId}";
        var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(BotToken));
        var hash = Convert.ToHexString(HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))).ToLower();

        return host.Controller<TelegramAuthController>()
            .Execute(x => x.Authenticate(
                new TelegramWidgetAuthRequest
                {
                    Id = telegramId,
                    FirstName = "Ada",
                    AuthDate = authDate,
                    Hash = hash,
                    SignUpSource = source,
                },
                default));
    }

    /// <summary>
    /// Mini App init data signed the way Telegram does (see <see cref="TelegramMiniAppAuthTests"/>), with a
    /// <c>start_param</c>.
    /// </summary>
    private string SignedInitData(long telegramId, string startParam)
    {
        var user = JsonSerializer.Serialize(new MiniAppUser { Id = telegramId, FirstName = "Ada" }, JsonBotAPI.Options);
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["user"] = user,
            ["auth_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["start_param"] = startParam,
        };

        var dataCheckString = string.Join("\n", fields.Select(x => $"{x.Key}={x.Value}"));
        var secretKey = HMACSHA256.HashData("WebAppData"u8.ToArray(), Encoding.UTF8.GetBytes(BotToken));
        fields["hash"] = Convert.ToHexString(HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))).ToLower();

        return string.Join("&", fields.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }
}
