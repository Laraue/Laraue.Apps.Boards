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
    [HttpGet]
    public Task<ServerCard> Get()
    {
        var publicMcpUrl = options.Value.PublicMcpUrl;

        var card = new ServerCard(
            Name: "com.laraue/boards",
            Version: "1.0.0",
            Description: "Read and update issues in a Laraue Boards organization - list/search " +
                          "issues, view details, create/edit issues and comments, and move issues " +
                          "between statuses.",
            Title: "Laraue Boards",
            WebsiteUrl: "https://boards.laraue.com",
            Remotes:
            [
                new ServerCardRemote(
                    Type: "streamable-http",
                    Url: publicMcpUrl,
                    Headers:
                    [
                        new ServerCardHeader(
                            Name: "X-Api-Key",
                            Description: "Boards API key - create one from your organization's settings.",
                            IsRequired: true,
                            IsSecret: true)
                    ],
                    SupportedProtocolVersions: ["2025-06-18"])
            ]);

        return Task.FromResult(card);
    }
}
