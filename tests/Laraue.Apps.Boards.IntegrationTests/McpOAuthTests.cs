using System.Net;
using System.Text;
using System.Text.Json;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// McpHost as an OAuth protected resource (BRD-362): a refused request points at the resource metadata, which names
/// the authorization server, and the access token it issues - an API key - works as a Bearer token. The test host's
/// public MCP URL is McpHost's own <c>http://localhost:5202/mcp</c>.
/// </summary>
[Collection("IntegrationTest")]
public class McpOAuthTests(WebApiTestHost webApiHost, McpHostTestHost mcpHost)
    : IClassFixture<WebApiTestHost>, IClassFixture<McpHostTestHost>
{
    private const string ResourceMetadataUrl = "http://localhost:5202/.well-known/oauth-protected-resource/mcp";

    [Fact]
    public async Task Mcp_ShouldChallengeWithResourceMetadata_WhenRequestHasNoKey()
    {
        using var client = mcpHost.CreateClient();

        var response = await client.PostAsync("/mcp", Initialize());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains($"resource_metadata=\"{ResourceMetadataUrl}\"", challenge.Parameter);
    }

    [Fact]
    public async Task Mcp_ShouldChallengeWithResourceMetadata_WhenBearerTokenIsUnknown()
    {
        using var client = mcpHost.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = Initialize() };
        request.Headers.Authorization = new("Bearer", "brdk_unknown");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("resource_metadata=", Assert.Single(response.Headers.WwwAuthenticate).Parameter);
    }

    [Fact]
    public async Task ResourceMetadata_ShouldNameResourceAndAuthorizationServer()
    {
        using var client = mcpHost.CreateClient();

        using var json = JsonDocument.Parse(await client.GetStringAsync(new Uri(ResourceMetadataUrl).AbsolutePath));

        Assert.Equal("http://localhost:5202/mcp", json.RootElement.GetProperty("resource").GetString());
        Assert.Equal(
            "https://boards.laraue.com",
            Assert.Single(json.RootElement.GetProperty("authorization_servers").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task ListTools_ShouldWork_WhenApiKeyIsSentAsBearerToken()
    {
        using var testScope = webApiHost.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);
        var apiKey = await testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(organization.Id, ownerId, "ChatGPT (chatgpt.com)", CancellationToken.None);

        await using var client = await mcpHost.ConnectMcpClientAsync(apiKey.RawKey, asBearer: true);
        var tools = await client.ListToolsAsync();

        Assert.Contains(tools, tool => tool.Name == "get_me");
    }

    private static StringContent Initialize()
    {
        const string body = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
            """;
        return new StringContent(body, Encoding.UTF8, "application/json");
    }
}
