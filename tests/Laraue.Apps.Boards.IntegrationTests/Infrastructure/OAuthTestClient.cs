using Laraue.Apps.Boards.Services.OAuth;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

/// <summary>
/// The OAuth client the test hosts know: ChatGPT, as listed in the integration tests' <c>OAuth:AllowedClientIds</c>.
/// </summary>
public static class OAuthTestClient
{
    public const string ClientId = "https://chatgpt.com/oauth/client.json";
    public const string RedirectUri = "https://chatgpt.com/connector_platform_oauth_redirect";
    public const string Resource = "https://boards.laraue.com/boards-mcp/mcp";

    public static readonly OAuthClientMetadata Metadata = new(ClientId, "ChatGPT", [RedirectUri]);
}
