using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.McpHost.Controllers;

/// <summary>
/// Serves this MCP server's discovery document at <c>GET /mcp/server-card</c> - the location the
/// (still-experimental, unfinalized) MCP Server Cards proposal recommends, so a client can learn
/// how to connect (URL, auth header, protocol version) without a human having to paste the URL in
/// by hand. See https://github.com/modelcontextprotocol/experimental-ext-server-card.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("/mcp/server-card")]
public class ServerCardController(IOptions<ServerCardOptions> options) : ControllerBase
{
    /// <summary>
    /// MCP protocol versions this server negotiates (ModelContextProtocol SDK 1.4). Update when bumping
    /// the SDK.
    /// </summary>
    private static readonly string[] SupportedProtocolVersions = ["2025-06-18", "2025-11-25"];

    [HttpGet]
    public Task<ServerCard> Get()
    {
        // Everything but the URL comes from server.json; the URL is per environment (ServerCardOptions).
        var manifest = ServerManifest.Current;
        var manifestRemote = manifest.Remotes.Single();

        var card = new ServerCard(
            Name: manifest.Name,
            Version: manifest.Version,
            Description: manifest.Description,
            Title: manifest.Title,
            WebsiteUrl: manifest.WebsiteUrl,
            Remotes:
            [
                new ServerCardRemote(
                    Type: manifestRemote.Type,
                    Url: options.Value.PublicMcpUrl,
                    Headers: manifestRemote.Headers,
                    SupportedProtocolVersions: SupportedProtocolVersions)
            ]);

        return Task.FromResult(card);
    }
}
