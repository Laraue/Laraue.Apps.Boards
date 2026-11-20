using System.Net;
using System.Text;
using Laraue.Apps.Boards.Services.OAuth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.IntegrationTests;

public class OAuthClientMetadataFetcherTests
{
    private const string ClientId = "https://chatgpt.com/oauth/client.json";

    [Fact]
    public async Task Fetch_ShouldReturnNameAndRedirectUris_WhenDocumentIsValid()
    {
        var fetcher = CreateFetcher(Json(new
        {
            client_id = ClientId,
            client_name = "ChatGPT",
            redirect_uris = new[] { "https://chatgpt.com/connector_platform_oauth_redirect" },
            token_endpoint_auth_method = "none",
        }));

        var result = await fetcher.Fetch(ClientId, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("ChatGPT", result.Metadata!.ClientName);
        Assert.Equal("chatgpt.com", result.Metadata.Host);
        Assert.Equal(["https://chatgpt.com/connector_platform_oauth_redirect"], result.Metadata.RedirectUris);
    }

    [Fact]
    public async Task Fetch_ShouldNameClientByHost_WhenDocumentHasNoName()
    {
        var fetcher = CreateFetcher(Json(new
        {
            client_id = ClientId,
            redirect_uris = new[] { "https://chatgpt.com/callback" },
        }));

        var result = await fetcher.Fetch(ClientId, CancellationToken.None);

        Assert.Equal("chatgpt.com", result.Metadata!.ClientName);
    }

    [Theory]
    [InlineData("""{"client_id": "https://evil.example/client.json", "redirect_uris": ["https://chatgpt.com/cb"]}""")]
    [InlineData("""{"client_id": "https://chatgpt.com/oauth/client.json", "redirect_uris": []}""")]
    [InlineData("""{"client_id": "https://chatgpt.com/oauth/client.json"}""")]
    [InlineData("""{"client_id": "https://chatgpt.com/oauth/client.json", "redirect_uris": ["javascript:alert(1)"]}""")]
    [InlineData("""{"client_id": "https://chatgpt.com/oauth/client.json", "redirect_uris": ["http://chatgpt.com/cb"]}""")]
    [InlineData("""{"client_id": "https://chatgpt.com/oauth/client.json", "redirect_uris": ["https://chatgpt.com/cb"], "token_endpoint_auth_method": "client_secret_basic"}""")]
    [InlineData("""{"client_id": "https://chatgpt.com/oauth/client.json", "redirect_uris": ["https://chatgpt.com/cb", null]}""")]
    [InlineData("""{"client_id": "https://chatgpt.com/oauth/client.json", "client_name": 5, "redirect_uris": ["https://chatgpt.com/cb"]}""")]
    [InlineData("""["not", "an", "object"]""")]
    [InlineData("not json")]
    public async Task Fetch_ShouldReturnInvalidDocument_WhenDocumentCannotBeUsed(string document)
    {
        var fetcher = CreateFetcher(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(document, Encoding.UTF8, "application/json"),
        });

        var result = await fetcher.Fetch(ClientId, CancellationToken.None);

        Assert.Equal(OAuthClientMetadataError.InvalidDocument, result.Error);
    }

    [Fact]
    public async Task Fetch_ShouldReturnUnreachable_WhenServerAnswersNotFound()
    {
        var fetcher = CreateFetcher(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await fetcher.Fetch(ClientId, CancellationToken.None);

        Assert.Equal(OAuthClientMetadataError.Unreachable, result.Error);
    }

    [Fact]
    public async Task Fetch_ShouldReturnUnreachable_WhenDocumentIsOverSizeLimit()
    {
        var fetcher = CreateFetcher(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string(' ', OAuthClientMetadataFetcher.MaxDocumentBytes + 1)),
        });

        var result = await fetcher.Fetch(ClientId, CancellationToken.None);

        Assert.Equal(OAuthClientMetadataError.Unreachable, result.Error);
    }

    [Fact]
    public async Task Fetch_ShouldUseCache_WhenDocumentWasFetchedBefore()
    {
        var calls = 0;
        var fetcher = CreateFetcher(request =>
        {
            calls++;
            return Json(new { client_id = ClientId, redirect_uris = new[] { "https://chatgpt.com/cb" } })(request);
        });

        await fetcher.Fetch(ClientId, CancellationToken.None);
        await fetcher.Fetch(ClientId, CancellationToken.None);

        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("https://evil.example/oauth/client.json")]
    [InlineData("https://chatgpt.com/oauth/client.json?x=1")]
    [InlineData("https://localhost/oauth/client.json")]
    public async Task Fetch_ShouldReturnUnknownClient_WhenClientIdIsNotAllowed(string clientId)
    {
        var fetcher = CreateFetcher(_ => throw new InvalidOperationException("Must not be fetched."));

        var result = await fetcher.Fetch(clientId, CancellationToken.None);

        Assert.Equal(OAuthClientMetadataError.UnknownClient, result.Error);
    }

    [Theory]
    [InlineData("https://chatgpt.com/cb", true)]
    [InlineData("https://chatgpt.com/cb?x=1", false)]
    [InlineData("http://localhost:53682/callback", true)]
    [InlineData("http://127.0.0.1:8080/callback", false)]
    [InlineData("http://localhost:53682/other", false)]
    public void AllowsRedirectUri_ShouldMatchExactly_ExceptLoopbackPort(string redirectUri, bool expected)
    {
        var metadata = new OAuthClientMetadata(
            ClientId,
            "Client",
            ["https://chatgpt.com/cb", "http://localhost/callback"]);

        Assert.Equal(expected, metadata.AllowsRedirectUri(redirectUri));
    }

    private static OAuthClientMetadataFetcher CreateFetcher(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        return new OAuthClientMetadataFetcher(
            new SingleHandlerFactory(new StubHandler(respond)),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new OAuthClientsOptions { AllowedClientIds = [ClientId] }),
            NullLogger<OAuthClientMetadataFetcher>.Instance);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Json(object document)
    {
        return _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(document), Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>
    /// Hands out clients over one handler that the fetcher's disposing of a client leaves alone, the way a factory-made
    /// client shares its handler.
    /// </summary>
    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, disposeHandler: false);
        }
    }
}
