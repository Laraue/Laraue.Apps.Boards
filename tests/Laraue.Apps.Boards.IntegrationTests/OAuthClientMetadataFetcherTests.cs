using System.Net;
using System.Text;
using Laraue.Apps.Boards.Services.OAuth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

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
    [InlineData("http://chatgpt.com/oauth/client.json")]
    [InlineData("https://chatgpt.com")]
    [InlineData("https://chatgpt.com/")]
    [InlineData("https://chatgpt.com:8443/oauth/client.json")]
    [InlineData("https://127.0.0.1/oauth/client.json")]
    [InlineData("https://user:pass@chatgpt.com/oauth/client.json")]
    [InlineData("https://chatgpt.com/oauth/client.json#x")]
    [InlineData("https://chatgpt.com/oauth/../client.json")]
    [InlineData("not a url")]
    public async Task Fetch_ShouldReturnInvalidClientId_WhenUrlBreaksRules(string clientId)
    {
        var fetcher = CreateFetcher(_ => throw new InvalidOperationException("Must not be fetched."));

        var result = await fetcher.Fetch(clientId, CancellationToken.None);

        Assert.Equal(OAuthClientMetadataError.InvalidClientId, result.Error);
    }

    [Fact]
    public async Task CreateHandler_ShouldRefuseConnection_WhenHostResolvesToPrivateAddress()
    {
        using var client = new HttpClient(OAuthClientMetadataFetcher.CreateHandler());

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("https://localhost/oauth/client.json"));

        Assert.Contains("doesn't resolve to public addresses only", exception.ToString());
    }

    [Fact]
    public async Task Fetch_ShouldReturnUnreachable_WhenHostResolvesToPrivateAddress()
    {
        using var handler = OAuthClientMetadataFetcher.CreateHandler();
        var fetcher = new OAuthClientMetadataFetcher(
            new SingleHandlerFactory(handler),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<OAuthClientMetadataFetcher>.Instance);

        var result = await fetcher.Fetch("https://localhost/oauth/client.json", CancellationToken.None);

        Assert.Equal(OAuthClientMetadataError.Unreachable, result.Error);
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.5", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("64:ff9b::a00:1", false)]
    public void IsPublicAddress_ShouldTellPublicFromInternal(string address, bool expected)
    {
        Assert.Equal(expected, OAuthClientMetadataFetcher.IsPublicAddress(IPAddress.Parse(address)));
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
