using System.Net;
using System.Security.Cryptography;
using System.Text;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiHost;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class ConnectedAccountsControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<ConnectedAccountsController> _controller = host.Controller<ConnectedAccountsController>();
    private readonly Proxy<UserController> _userController = host.Controller<UserController>();

    [Fact]
    public async Task ConnectGoogle_ShouldConnectGoogleAccount_WhenTokenIsValid()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        SetupGoogleToken("valid-token", "google-20");

        var response = await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.ConnectGoogle(new ConnectGoogleAccountRequest { IdToken = "valid-token" }, default));

        Assert.Equal(AccountLinkOutcome.Linked, response!.Outcome);
        var user = await _userController.WithUserAuthorization(userId).Execute(x => x.GetAsync(default));
        Assert.True(user!.HasGoogleAccount);
    }

    [Fact]
    public async Task ConnectGoogle_ShouldReturnOwnerHasData_WhenAnotherUserWithDataHasTheAccount()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser(u => u.GoogleSubject = "google-21");
        await testScope.InitializeOrganization(ownerId, organization => organization.AddIssueToDefaultStatus(ownerId));
        var userId = await testScope.CreateUser();
        SetupGoogleToken("owned-token", "google-21");

        var response = await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.ConnectGoogle(new ConnectGoogleAccountRequest { IdToken = "owned-token" }, default));

        Assert.Equal(AccountLinkOutcome.OwnerHasData, response!.Outcome);
        Assert.Null((await testScope.Database.Users.SingleAsync(x => x.Id == userId)).GoogleSubject);
    }

    [Fact]
    public async Task ConnectGoogle_ShouldReturnForbidden_WhenTokenIsInvalid()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        host.GoogleIdTokenValidatorMock
            .Setup(x => x.ValidateAsync("invalid-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Google ID token is invalid"));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.ConnectGoogle(new ConnectGoogleAccountRequest { IdToken = "invalid-token" }, default)));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task ConnectTelegram_ShouldConnectTelegramAccount_WhenWidgetDataIsSigned()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(u =>
        {
            u.TelegramId = null;
            u.GoogleSubject = "google-22";
        });

        var response = await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.ConnectTelegram(SignedWidgetData(522), default));

        Assert.Equal(AccountLinkOutcome.Linked, response!.Outcome);
        Assert.Equal(522, (await testScope.Database.Users.SingleAsync(x => x.Id == userId)).TelegramId);
    }

    [Fact]
    public async Task ConnectTelegram_ShouldReturnForbidden_WhenSignatureIsWrong()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(u => u.TelegramId = null);
        var signed = SignedWidgetData(523);
        var tampered = new TelegramWidgetAuthRequest
        {
            Id = 524,
            FirstName = signed.FirstName,
            AuthDate = signed.AuthDate,
            Hash = signed.Hash,
        };

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.ConnectTelegram(tampered, default)));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Null((await testScope.Database.Users.SingleAsync(x => x.Id == userId)).TelegramId);
    }

    [Fact]
    public async Task ConnectTelegram_ShouldReturnUnauthorized_WhenUserIsNotSignedIn()
    {
        using var testScope = host.CreateTestScope();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _controller
            .Execute(x => x.ConnectTelegram(SignedWidgetData(525), default)));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    private void SetupGoogleToken(string idToken, string googleSubject)
    {
        host.GoogleIdTokenValidatorMock
            .Setup(x => x.ValidateAsync(idToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdTokenPayload(googleSubject, "ada@example.com", "Ada Lovelace", "Ada", "Lovelace"));
    }

    /// <summary>
    /// Login widget data signed the way Telegram signs it, with the test bot token - so the request goes
    /// through the real signature check.
    /// </summary>
    private TelegramWidgetAuthRequest SignedWidgetData(long telegramId)
    {
        var botToken = host.Services.GetRequiredService<IConfiguration>()["Telegram:Token"]!;
        var authDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dataCheckString = $"auth_date={authDate}\nfirst_name=Ada\nid={telegramId}";
        var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));
        var hash = Convert.ToHexString(HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))).ToLower();

        return new TelegramWidgetAuthRequest { Id = telegramId, FirstName = "Ada", AuthDate = authDate, Hash = hash };
    }
}
