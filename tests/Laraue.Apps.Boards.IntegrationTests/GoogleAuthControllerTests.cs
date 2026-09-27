using System.Net;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class GoogleAuthControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<GoogleAuthController> _controller = host.Controller<GoogleAuthController>();

    [Fact]
    public async Task Authenticate_ShouldRegisterGoogleUser_WhenGoogleAccountIsNew()
    {
        using var testScope = host.CreateTestScope();
        SetupValidToken("valid-token", new GoogleIdTokenPayload("google-10", "ann@example.com", "Ann Lee", "Ann", "Lee"));

        var token = await _controller.Execute(x => x.Authenticate(
            new GoogleAuthRequest { IdToken = "valid-token", LanguageCode = "en" },
            default));

        Assert.False(string.IsNullOrEmpty(token));
        var user = await testScope.Database.Users.SingleAsync(x => x.GoogleSubject == "google-10");
        Assert.Equal("Ann Lee", user.DisplayName);
        Assert.Null(user.TelegramId);
    }

    [Fact]
    public async Task Authenticate_ShouldNotRegisterAgain_WhenGoogleAccountIsAlreadyRegistered()
    {
        using var testScope = host.CreateTestScope();
        SetupValidToken("valid-token", new GoogleIdTokenPayload("google-11", "ann@example.com", "Ann Lee", "Ann", "Lee"));
        var request = new GoogleAuthRequest { IdToken = "valid-token" };

        await _controller.Execute(x => x.Authenticate(request, default));
        await _controller.Execute(x => x.Authenticate(request, default));

        Assert.Equal(1, await testScope.Database.Users.CountAsync(x => x.GoogleSubject == "google-11"));
    }

    [Fact]
    public async Task Authenticate_ShouldRegisterGoogleUser_WhenAuthorizationCodeIsSent()
    {
        using var testScope = host.CreateTestScope();
        host.GoogleIdTokenValidatorMock
            .Setup(x => x.ExchangeCodeAsync("valid-code", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdTokenPayload("google-12", "bob@example.com", "Bob Ray", "Bob", "Ray"));

        var token = await _controller.Execute(x => x.Authenticate(
            new GoogleAuthRequest { Code = "valid-code" },
            default));

        Assert.False(string.IsNullOrEmpty(token));
        Assert.True(await testScope.Database.Users.AnyAsync(x => x.GoogleSubject == "google-12"));
    }

    [Fact]
    public async Task Authenticate_ShouldReturnBadRequest_WhenNoCredentialIsSent()
    {
        using var testScope = host.CreateTestScope();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _controller.Execute(x => x.Authenticate(
            new GoogleAuthRequest(),
            default)));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task Authenticate_ShouldReturnForbidden_WhenIdTokenIsInvalid()
    {
        using var testScope = host.CreateTestScope();
        host.GoogleIdTokenValidatorMock
            .Setup(x => x.ValidateAsync("invalid-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Google ID token is invalid"));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _controller.Execute(x => x.Authenticate(
            new GoogleAuthRequest { IdToken = "invalid-token" },
            default)));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.False(await testScope.Database.Users.AnyAsync());
    }

    [Fact]
    public async Task ValidateAsync_ShouldThrowForbidden_WhenIdTokenIsMalformed()
    {
        var validator = new GoogleIdTokenValidator(
            Options.Create(new GoogleAuthOptions { ClientId = "test-client-id.apps.googleusercontent.com" }));

        await Assert.ThrowsAsync<ForbiddenException>(() => validator.ValidateAsync("not-a-jwt", default));
    }

    [Fact]
    public async Task ExchangeCodeAsync_ShouldRefuseToExchange_WhenClientSecretIsNotConfigured()
    {
        var validator = new GoogleIdTokenValidator(
            Options.Create(new GoogleAuthOptions { ClientId = "test-client-id.apps.googleusercontent.com" }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ExchangeCodeAsync("code", default));
    }

    [Fact]
    public async Task ValidateAsync_ShouldRefuseToValidate_WhenClientIdIsNotConfigured()
    {
        var validator = new GoogleIdTokenValidator(Options.Create(new GoogleAuthOptions { ClientId = "" }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync("not-a-jwt", default));
    }

    private void SetupValidToken(string idToken, GoogleIdTokenPayload payload)
    {
        host.GoogleIdTokenValidatorMock
            .Setup(x => x.ValidateAsync(idToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);
    }
}
