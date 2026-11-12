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
/// Where a user came from (BRD-295), on the web sign-in paths: Boards sends what it saw to Identity, raw (plus its own
/// invite rule), and keeps the source Identity returns.
/// The bot's <c>/start</c> parameter is covered in <see cref="SignUpSourceTelegramTests"/>.
/// </summary>
[Collection("IntegrationTest")]
public class SignUpSourceTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task WidgetSignIn_ShouldSendLandingUrlAndReferrerAsIs_WhenNewUserSignsUp()
    {
        using var testScope = host.CreateTestScope();
        const long telegramId = 1201;
        IdentityReturnsSource(telegramId, "producthunt");

        var token = await AuthenticateWithWidget(
            telegramId,
            "https://boards.laraue.com/?ref=ProductHunt",
            "https://www.producthunt.com/posts/boards");

        Assert.False(string.IsNullOrEmpty(token));
        IdentityClientMock.Verify(
            x => x.CreateUserIfNotExistsAsync(
                It.Is<CreateUserIfNotExistsRequest>(r => r.TelegramId == telegramId
                    && !r.HasSource
                    && r.LandingUrl == "https://boards.laraue.com/?ref=ProductHunt"
                    && r.Referrer == "https://www.producthunt.com/posts/boards"),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        // Identity works the source out; Boards keeps what it returns.
        Assert.Equal("producthunt", await GetSource(testScope, telegramId));
    }

    [Fact]
    public async Task WidgetSignIn_ShouldSendInviteAsSource_WhenUserLandedOnTheInvitePage()
    {
        using var testScope = host.CreateTestScope();
        const long telegramId = 1202;

        await AuthenticateWithWidget(telegramId, "https://boards.laraue.com/join/abc123", "https://t.me/");

        IdentityClientMock.Verify(
            x => x.CreateUserIfNotExistsAsync(
                It.Is<CreateUserIfNotExistsRequest>(r => r.TelegramId == telegramId && r.Source == "invite"),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WidgetSignIn_ShouldKeepFirstSource_WhenUserSignsInAgainFromAnotherLink()
    {
        using var testScope = host.CreateTestScope();
        const long telegramId = 1203;
        IdentityReturnsSource(telegramId, "productradar");

        await AuthenticateWithWidget(telegramId, "https://boards.laraue.com/?ref=productradar", null);
        IdentityReturnsSource(telegramId, "producthunt");
        await AuthenticateWithWidget(telegramId, "https://boards.laraue.com/?ref=producthunt", null);

        // The second sign-in finds the user and doesn't create it again.
        Assert.Equal("productradar", await GetSource(testScope, telegramId));
    }

    [Fact]
    public async Task WidgetSignIn_ShouldStoreIdentitysSource_WhenIdentityKeptAnEarlierOne()
    {
        // The user's Boards row in Identity already had a source (e.g. an earlier, interrupted sign-up): Identity
        // returns that one, and Boards keeps the same value.
        using var testScope = host.CreateTestScope();
        const long telegramId = 1205;
        IdentityReturnsSource(telegramId, "telegram");

        await AuthenticateWithWidget(telegramId, "https://boards.laraue.com/?ref=producthunt", null);

        Assert.Equal("telegram", await GetSource(testScope, telegramId));
    }

    [Fact]
    public async Task MiniAppSignIn_ShouldSendStartParamAsSource_WhenTheAppWasOpenedWithOne()
    {
        using var testScope = host.CreateTestScope();
        const long telegramId = 1206;

        await host.Controller<TelegramAuthController>()
            .Execute(x => x.Authenticate(
                new AuthenticateViaStringInitDataRequest
                {
                    InitData = SignedInitData(telegramId, startParam: "channel_post"),
                    SignUpLandingUrl = "https://boards.laraue.com/join/abc123",
                },
                default));

        // Telegram's signed start parameter wins even over the invite page.
        IdentityClientMock.Verify(
            x => x.CreateUserIfNotExistsAsync(
                It.Is<CreateUserIfNotExistsRequest>(r => r.TelegramId == telegramId && r.Source == "channel_post"),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MiniAppSignIn_ShouldSendLandingUrlAndReferrer_WhenTheAppHasNoStartParam()
    {
        using var testScope = host.CreateTestScope();
        const long telegramId = 1207;

        await host.Controller<TelegramAuthController>()
            .Execute(x => x.Authenticate(
                new AuthenticateViaStringInitDataRequest
                {
                    InitData = SignedInitData(telegramId, startParam: null),
                    SignUpLandingUrl = "https://boards.laraue.com/?utm_source=newsletter",
                    SignUpReferrer = "https://mail.example.com/",
                },
                default));

        IdentityClientMock.Verify(
            x => x.CreateUserIfNotExistsAsync(
                It.Is<CreateUserIfNotExistsRequest>(r => r.TelegramId == telegramId
                    && !r.HasSource
                    && r.LandingUrl == "https://boards.laraue.com/?utm_source=newsletter"
                    && r.Referrer == "https://mail.example.com/"),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GoogleSignIn_ShouldSendLandingUrlAndReferrerAndStoreIdentitysSource_WhenNewUserSignsUp()
    {
        using var testScope = host.CreateTestScope();
        host.GoogleIdTokenValidatorMock
            .Setup(x => x.ValidateAsync("source-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdTokenPayload("google-source-1", "ann@example.com", "Ann Lee", "Ann", "Lee"));
        IdentityClientMock
            .Setup(x => x.CreateUserIfNotExistsByGoogleAsync(
                It.Is<CreateUserIfNotExistsByGoogleRequest>(r => r.GoogleSubject == "google-source-1"),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(GrpcTestHelpers.AsyncUnaryCallOf(new CreateUserIfNotExistsResponse
            {
                UserId = Guid.NewGuid().ToString(),
                Source = "habr",
            }));

        await host.Controller<GoogleAuthController>()
            .Execute(x => x.Authenticate(
                new GoogleAuthRequest
                {
                    IdToken = "source-token",
                    SignUpLandingUrl = "https://boards.laraue.com/ru",
                    SignUpReferrer = "https://habr.com/ru/articles/1/",
                },
                default));

        IdentityClientMock.Verify(
            x => x.CreateUserIfNotExistsByGoogleAsync(
                It.Is<CreateUserIfNotExistsByGoogleRequest>(r => r.GoogleSubject == "google-source-1"
                    && r.LandingUrl == "https://boards.laraue.com/ru"
                    && r.Referrer == "https://habr.com/ru/articles/1/"),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        var source = await testScope.Database.Users
            .Where(x => x.GoogleSubject == "google-source-1")
            .Select(x => x.SignUpSource)
            .SingleAsync();
        Assert.Equal("habr", source);
    }

    /// <summary>
    /// Makes the mocked Identity answer this Telegram user's create call with <paramref name="source"/>, as the real one
    /// would after working it out.
    /// </summary>
    private void IdentityReturnsSource(long telegramId, string source)
    {
        IdentityClientMock
            .Setup(x => x.CreateUserIfNotExistsAsync(
                It.Is<CreateUserIfNotExistsRequest>(r => r.TelegramId == telegramId),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => GrpcTestHelpers.AsyncUnaryCallOf(new CreateUserIfNotExistsResponse
            {
                UserId = Guid.NewGuid().ToString(),
                Source = source,
            }));
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
    private Task<string?> AuthenticateWithWidget(long telegramId, string? landingUrl, string? referrer)
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
                    SignUpLandingUrl = landingUrl,
                    SignUpReferrer = referrer,
                },
                default));
    }

    /// <summary>
    /// Mini App init data signed the way Telegram does (see <see cref="TelegramMiniAppAuthTests"/>), with a
    /// <c>start_param</c>.
    /// </summary>
    private string SignedInitData(long telegramId, string? startParam)
    {
        var user = JsonSerializer.Serialize(new MiniAppUser { Id = telegramId, FirstName = "Ada" }, JsonBotAPI.Options);
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["user"] = user,
            ["auth_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
        };
        if (startParam is not null)
            fields["start_param"] = startParam;

        var dataCheckString = string.Join("\n", fields.Select(x => $"{x.Key}={x.Value}"));
        var secretKey = HMACSHA256.HashData("WebAppData"u8.ToArray(), Encoding.UTF8.GetBytes(BotToken));
        fields["hash"] = Convert.ToHexString(HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))).ToLower();

        return string.Join("&", fields.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }
}
