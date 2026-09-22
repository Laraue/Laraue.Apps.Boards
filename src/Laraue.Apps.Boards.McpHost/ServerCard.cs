using System.Text.Json.Serialization;

namespace Laraue.Apps.Boards.McpHost;

public sealed class ServerCardOptions
{
    public string PublicMcpUrl { get; set; } = "http://localhost:5202/mcp";
}

/// <summary>
/// Static discovery document for this MCP server, served at <c>GET /mcp/server-card</c>
/// (<see cref="Controllers.ServerCardController"/>) - the location the (still-experimental,
/// unfinalized) MCP Server Cards proposal recommends, so a client can learn how to connect (URL,
/// auth header, protocol version) without a human having to paste the URL in by hand. See
/// https://github.com/modelcontextprotocol/experimental-ext-server-card.
/// </summary>
public sealed record ServerCard(
    string Name,
    string Version,
    string Description,
    string Title,
    string WebsiteUrl,
    IReadOnlyList<ServerCardRemote> Remotes)
{
    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = "https://static.modelcontextprotocol.io/schemas/v1/server-card.schema.json";
}

public sealed record ServerCardRemote(
    string Type,
    string Url,
    IReadOnlyList<ServerCardHeader> Headers,
    IReadOnlyList<string> SupportedProtocolVersions);

public sealed record ServerCardHeader(
    string Name,
    string Description,
    bool IsRequired,
    bool IsSecret);
