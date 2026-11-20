using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.OAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class CoreOAuthAuthorizationServiceTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private const string ClientId = "https://chatgpt.com/oauth/client.json";
    private const string RedirectUri = "https://chatgpt.com/connector_platform_oauth_redirect";
    private const string Resource = "https://boards.laraue.com/boards-mcp/mcp";
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    [Fact]
    public async Task Redeem_ShouldIssueKeyForApprovingUserAndOrganization_WhenCodeAndVerifierMatch()
    {
        using var testScope = host.CreateTestScope();
        var setup = await ApproveAsync(testScope);

        var result = await RedeemAsync(testScope, TokenRequest(setup.Code));

        Assert.Equal(OAuthTokenOutcome.Issued, result.Outcome);
        var principal = await Service<ICoreApiKeysService>(testScope)
            .ValidateAsync(result.ApiKey!.RawKey, CancellationToken.None);
        Assert.NotNull(principal);
        Assert.Equal(setup.OrganizationId, principal.OrganizationId);
        Assert.Equal(setup.UserId, principal.UserId);

        var keyName = await testScope.Database.ApiKeys
            .Where(x => x.Id == result.ApiKey.Id)
            .Select(x => x.Name)
            .SingleAsync();
        Assert.Equal("ChatGPT (chatgpt.com)", keyName);
    }

    [Fact]
    public async Task Redeem_ShouldRefuse_WhenVerifierDoesNotMatch()
    {
        using var testScope = host.CreateTestScope();
        var setup = await ApproveAsync(testScope);

        var result = await RedeemAsync(
            testScope,
            TokenRequest(setup.Code) with { CodeVerifier = new string('a', 43) });

        Assert.Equal(OAuthTokenOutcome.InvalidGrant, result.Outcome);
    }

    [Theory]
    [InlineData("client")]
    [InlineData("redirect")]
    [InlineData("resource")]
    public async Task Redeem_ShouldRefuse_WhenTokenRequestDiffersFromAuthorization(string field)
    {
        using var testScope = host.CreateTestScope();
        var setup = await ApproveAsync(testScope);
        var request = TokenRequest(setup.Code);
        request = field switch
        {
            "client" => request with { ClientId = "https://evil.example/client.json" },
            "redirect" => request with { RedirectUri = "https://evil.example/callback" },
            _ => request with { Resource = "https://evil.example/mcp" },
        };

        var result = await RedeemAsync(testScope, request);

        Assert.Equal(OAuthTokenOutcome.InvalidGrant, result.Outcome);
    }

    [Fact]
    public async Task Redeem_ShouldAcceptRequest_WhenResourceIsOmitted()
    {
        using var testScope = host.CreateTestScope();
        var setup = await ApproveAsync(testScope);

        var result = await RedeemAsync(testScope, TokenRequest(setup.Code) with { Resource = null });

        Assert.Equal(OAuthTokenOutcome.Issued, result.Outcome);
    }

    [Fact]
    public async Task Redeem_ShouldRefuse_WhenRequestExpired()
    {
        using var testScope = host.CreateTestScope();
        var setup = await ApproveAsync(testScope);
        await testScope.Database.OAuthAuthorizationRequests
            .Where(x => x.Id == setup.RequestId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));

        var result = await RedeemAsync(testScope, TokenRequest(setup.Code));

        Assert.Equal(OAuthTokenOutcome.InvalidGrant, result.Outcome);
    }

    [Fact]
    public async Task Redeem_ShouldRevokeIssuedKey_WhenCodeIsUsedAgain()
    {
        using var testScope = host.CreateTestScope();
        var setup = await ApproveAsync(testScope);
        var first = await RedeemAsync(testScope, TokenRequest(setup.Code));

        var second = await RedeemAsync(testScope, TokenRequest(setup.Code));

        Assert.Equal(OAuthTokenOutcome.InvalidGrant, second.Outcome);
        Assert.Null(await Service<ICoreApiKeysService>(testScope)
            .ValidateAsync(first.ApiKey!.RawKey, CancellationToken.None));
    }

    [Fact]
    public async Task Redeem_ShouldRevokePreviousKey_WhenSameClientConnectsAgain()
    {
        using var testScope = host.CreateTestScope();
        var setup = await ApproveAsync(testScope);
        var first = await RedeemAsync(testScope, TokenRequest(setup.Code));

        var again = await ApproveAsync(testScope, setup.UserId, setup.OrganizationId);
        var second = await RedeemAsync(testScope, TokenRequest(again.Code));

        var keys = Service<ICoreApiKeysService>(testScope);
        Assert.Null(await keys.ValidateAsync(first.ApiKey!.RawKey, CancellationToken.None));
        Assert.NotNull(await keys.ValidateAsync(second.ApiKey!.RawKey, CancellationToken.None));
    }

    [Fact]
    public async Task Redeem_ShouldKeepOtherClientsKey_WhenAnotherClientConnects()
    {
        using var testScope = host.CreateTestScope();
        var setup = await ApproveAsync(testScope);
        var chatGpt = await RedeemAsync(testScope, TokenRequest(setup.Code));

        var claude = await ApproveAsync(
            testScope,
            setup.UserId,
            setup.OrganizationId,
            "https://claude.ai/oauth/mcp-oauth-client-metadata",
            "https://claude.ai/api/mcp/auth_callback");
        await RedeemAsync(testScope, new OAuthTokenRequest(
            claude.Code,
            "https://claude.ai/oauth/mcp-oauth-client-metadata",
            "https://claude.ai/api/mcp/auth_callback",
            Verifier,
            Resource));

        Assert.NotNull(await Service<ICoreApiKeysService>(testScope)
            .ValidateAsync(chatGpt.ApiKey!.RawKey, CancellationToken.None));
    }

    [Fact]
    public async Task Redeem_ShouldKeepEarlierKey_WhenNativeClientConnectsFromAnotherMachine()
    {
        const string claudeCode = "https://claude.ai/oauth/claude-code-client-metadata";
        using var testScope = host.CreateTestScope();
        var laptop = await ApproveAsync(testScope, clientId: claudeCode, redirectUri: "http://localhost:53682/callback");
        var laptopKey = await RedeemAsync(testScope, new OAuthTokenRequest(
            laptop.Code,
            claudeCode,
            "http://localhost:53682/callback",
            Verifier,
            Resource));

        var desktop = await ApproveAsync(
            testScope,
            laptop.UserId,
            laptop.OrganizationId,
            claudeCode,
            "http://localhost:61000/callback");
        var desktopKey = await RedeemAsync(testScope, new OAuthTokenRequest(
            desktop.Code,
            claudeCode,
            "http://localhost:61000/callback",
            Verifier,
            Resource));

        var keys = Service<ICoreApiKeysService>(testScope);
        Assert.NotNull(await keys.ValidateAsync(laptopKey.ApiKey!.RawKey, CancellationToken.None));
        Assert.NotNull(await keys.ValidateAsync(desktopKey.ApiKey!.RawKey, CancellationToken.None));
    }

    [Fact]
    public async Task Approve_ShouldReturnNull_WhenRequestWasDenied()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var service = Service<ICoreOAuthAuthorizationService>(testScope);
        var requestId = await service.Start(Start(), CancellationToken.None);

        Assert.True(await service.Deny(requestId, userId, CancellationToken.None));
        var code = await service.Approve(requestId, userId, organization.Id, CancellationToken.None);

        Assert.Null(code);
        var state = await testScope.Database.OAuthAuthorizationRequests
            .Where(x => x.Id == requestId)
            .Select(x => x.State)
            .SingleAsync();
        Assert.Equal(OAuthAuthorizationState.Denied, state);
    }

    [Fact]
    public void IsVerifierValid_ShouldMatchRfc7636Example()
    {
        Assert.True(CoreOAuthAuthorizationService.IsVerifierValid(
            "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk",
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjX!")]
    public void IsVerifierValid_ShouldRefuse_WhenVerifierIsMalformed(string verifier)
    {
        Assert.False(CoreOAuthAuthorizationService.IsVerifierValid(verifier, Challenge(verifier)));
    }

    [Theory]
    [InlineData("ChatGPT", "https://chatgpt.com/oauth/client.json", "ChatGPT (chatgpt.com)")]
    [InlineData(
        "A client with a very long name that does not fit the column",
        "https://example.com/client.json",
        "A client with a very long name that does not fit t (example.com)")]
    public void KeyName_ShouldKeepHost_WhenNameIsLong(string clientName, string clientId, string expected)
    {
        var name = CoreOAuthAuthorizationService.KeyName(clientName, clientId);

        Assert.Equal(expected, name);
        Assert.True(name.Length <= 64);
    }

    private async Task<ApprovedSetup> ApproveAsync(
        WebApiTestHostScope testScope,
        Guid? userId = null,
        long? organizationId = null,
        string clientId = ClientId,
        string redirectUri = RedirectUri)
    {
        var user = userId ?? await testScope.CreateUser();
        var organization = organizationId ?? (await testScope.InitializeOrganization(user)).Id;
        var service = Service<ICoreOAuthAuthorizationService>(testScope);

        var requestId = await service.Start(
            Start() with { ClientId = clientId, RedirectUri = redirectUri },
            CancellationToken.None);
        var code = await service.Approve(requestId, user, organization, CancellationToken.None);

        return new ApprovedSetup(requestId, code!, user, organization);
    }

    private static async Task<OAuthTokenResult> RedeemAsync(WebApiTestHostScope testScope, OAuthTokenRequest request)
    {
        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();
        var result = await Service<ICoreOAuthAuthorizationService>(testScope).Redeem(request, CancellationToken.None);
        await transaction.CommitAsync();
        return result;
    }

    private static OAuthAuthorizationStart Start()
    {
        return new OAuthAuthorizationStart(ClientId, "ChatGPT", RedirectUri, "xyz", Challenge(Verifier), Resource);
    }

    private static OAuthTokenRequest TokenRequest(string code)
    {
        return new OAuthTokenRequest(code, ClientId, RedirectUri, Verifier, Resource);
    }

    private static string Challenge(string verifier)
    {
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    private static T Service<T>(WebApiTestHostScope testScope)
        where T : notnull
    {
        return testScope.Services.GetRequiredService<T>();
    }

    private sealed record ApprovedSetup(Guid RequestId, string Code, Guid UserId, long OrganizationId);
}
