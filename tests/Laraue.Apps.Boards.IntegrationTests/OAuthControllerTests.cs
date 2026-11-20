using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices.OAuth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// The OAuth endpoints as a client sees them: the authorize and token requests over raw HTTP (redirects, form posts,
/// OAuth's snake-case JSON), the consent page's calls through the controller proxy.
/// </summary>
[Collection("IntegrationTest")]
public class OAuthControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private const string ConsentPage = "https://boards.example.com/oauth/consent";
    private const string Issuer = "https://boards.laraue.com";
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    private readonly Proxy<OAuthController> _controller = host.Controller<OAuthController>();
    private readonly HttpClient _client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Metadata_ShouldAdvertiseCodeFlowWithPkceAndClientMetadataDocuments()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/.well-known/oauth-authorization-server"));
        var root = json.RootElement;

        Assert.Equal(Issuer, root.GetProperty("issuer").GetString());
        Assert.Equal("https://boards.laraue.com/boards-api/api/oauth/authorize", root.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("https://boards.laraue.com/boards-api/api/oauth/token", root.GetProperty("token_endpoint").GetString());
        Assert.Equal(["S256"], Strings(root.GetProperty("code_challenge_methods_supported")));
        Assert.Equal(["none"], Strings(root.GetProperty("token_endpoint_auth_methods_supported")));
        Assert.True(root.GetProperty("client_id_metadata_document_supported").GetBoolean());
        Assert.True(root.GetProperty("authorization_response_iss_parameter_supported").GetBoolean());
    }

    [Fact]
    public async Task Authorize_ShouldRedirectToConsentPage_WhenRequestIsValid()
    {
        var location = await AuthorizeAsync(AuthorizeParameters());

        Assert.StartsWith($"{ConsentPage}?request=", location);
        Assert.True(Guid.TryParse(Query(location)["request"], out _));
    }

    [Fact]
    public async Task Authorize_ShouldShowErrorOnOwnPage_WhenClientIsNotAllowed()
    {
        var parameters = AuthorizeParameters();
        parameters["client_id"] = "https://evil.example/client.json";
        parameters["redirect_uri"] = "https://evil.example/callback";

        var location = await AuthorizeAsync(parameters);

        Assert.Equal($"{ConsentPage}?error=unknown_client", location);
    }

    [Fact]
    public async Task Authorize_ShouldShowErrorOnOwnPage_WhenRedirectUriIsNotClients()
    {
        var parameters = AuthorizeParameters();
        parameters["redirect_uri"] = "https://evil.example/callback";

        var location = await AuthorizeAsync(parameters);

        Assert.Equal($"{ConsentPage}?error=invalid_redirect_uri", location);
    }

    [Theory]
    [InlineData("code_challenge_method", "plain", "invalid_request")]
    [InlineData("code_challenge", "", "invalid_request")]
    [InlineData("response_type", "token", "unsupported_response_type")]
    [InlineData("resource", "https://other.example/mcp", "invalid_target")]
    public async Task Authorize_ShouldRedirectToClientWithError_WhenRequestIsInvalid(
        string parameter,
        string value,
        string expectedError)
    {
        var parameters = AuthorizeParameters();
        parameters[parameter] = value;

        var location = await AuthorizeAsync(parameters);

        Assert.StartsWith($"{OAuthTestClient.RedirectUri}?", location);
        var query = Query(location);
        Assert.Equal(expectedError, query["error"]);
        Assert.Equal("xyz", query["state"]);
        Assert.Equal(Issuer, query["iss"]);
    }

    [Fact]
    public async Task Token_ShouldIssueApiKey_WhenConsentWasApproved()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var requestId = await StartAsync();

        var consent = await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.GetConsent(requestId, CancellationToken.None));
        var decision = await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.Approve(requestId, new ApproveOAuthRequest { OrganizationId = organization.Id }, CancellationToken.None));

        Assert.Equal("ChatGPT", consent.ClientName);
        Assert.Equal("chatgpt.com", consent.ClientHost);
        Assert.Equal("ChatGPT (chatgpt.com)", consent.KeyName);

        Assert.StartsWith($"{OAuthTestClient.RedirectUri}?", decision.RedirectUrl);
        var query = Query(decision.RedirectUrl);
        Assert.Equal("xyz", query["state"]);
        Assert.Equal(Issuer, query["iss"]);

        var response = await TokenAsync(TokenParameters(query["code"]!));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Bearer", json.RootElement.GetProperty("token_type").GetString());
        var principal = await testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .ValidateAsync(json.RootElement.GetProperty("access_token").GetString()!, CancellationToken.None);
        Assert.Equal(organization.Id, principal?.OrganizationId);
        Assert.Equal(userId, principal?.UserId);
    }

    [Fact]
    public async Task Approve_ShouldReturnNotFound_WhenUserIsNotMemberOfOrganization()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var strangerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);
        var requestId = await StartAsync();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _controller
            .WithUserAuthorization(strangerId)
            .Execute(x => x.Approve(requestId, new ApproveOAuthRequest { OrganizationId = organization.Id }, CancellationToken.None)));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Deny_ShouldRedirectToClientWithAccessDenied()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var requestId = await StartAsync();

        var decision = await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.Deny(requestId, CancellationToken.None));

        var query = Query(decision.RedirectUrl);
        Assert.Equal("access_denied", query["error"]);
        Assert.Equal("xyz", query["state"]);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.GetConsent(requestId, CancellationToken.None)));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Token_ShouldReturnInvalidGrant_WhenCodeIsUnknown()
    {
        var response = await TokenAsync(TokenParameters("unknown"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("""{"error":"invalid_grant"}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Token_ShouldReturnUnsupportedGrantType_WhenGrantIsNotAuthorizationCode()
    {
        var parameters = TokenParameters("any");
        parameters["grant_type"] = "client_credentials";

        var response = await TokenAsync(parameters);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("""{"error":"unsupported_grant_type"}""", await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> StartAsync()
    {
        var location = await AuthorizeAsync(AuthorizeParameters());
        return Guid.Parse(Query(location)["request"]!);
    }

    private async Task<string> AuthorizeAsync(Dictionary<string, string?> parameters)
    {
        var response = await _client.GetAsync(QueryHelpers.AddQueryString("/api/oauth/authorize", parameters));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.ToString();
    }

    private Task<HttpResponseMessage> TokenAsync(Dictionary<string, string> parameters)
    {
        return _client.PostAsync("/api/oauth/token", new FormUrlEncodedContent(parameters));
    }

    private static Dictionary<string, string?> AuthorizeParameters()
    {
        return new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = OAuthTestClient.ClientId,
            ["redirect_uri"] = OAuthTestClient.RedirectUri,
            ["code_challenge"] = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier))),
            ["code_challenge_method"] = "S256",
            ["state"] = "xyz",
            ["resource"] = OAuthTestClient.Resource,
        };
    }

    private static Dictionary<string, string> TokenParameters(string code)
    {
        return new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = OAuthTestClient.ClientId,
            ["redirect_uri"] = OAuthTestClient.RedirectUri,
            ["code_verifier"] = Verifier,
            ["resource"] = OAuthTestClient.Resource,
        };
    }

    private static Dictionary<string, string?> Query(string url)
    {
        return QueryHelpers.ParseQuery(new Uri(url).Query)
            .ToDictionary(x => x.Key, x => (string?)x.Value.ToString());
    }

    private static string?[] Strings(JsonElement array)
    {
        return array.EnumerateArray().Select(x => x.GetString()).ToArray();
    }
}
