using System.Text.Json;

namespace Laraue.Apps.Boards.McpHost;

/// <summary>
/// This server's <c>server.json</c> - the manifest published to the MCP registry (<c>mcp-publisher</c>),
/// embedded into the assembly so <see cref="Controllers.ServerCardController"/> serves the same name,
/// version, description and auth header instead of keeping its own copy that drifts. Bump the version
/// in <c>server.json</c> only.
/// </summary>
public sealed record ServerManifest(
    string Name,
    string Title,
    string Description,
    string Version,
    string WebsiteUrl,
    IReadOnlyList<ServerManifestRemote> Remotes)
{
    private const string ResourceName = "server.json";

    private static readonly Lazy<ServerManifest> CurrentValue = new(Load);

    public static ServerManifest Current => CurrentValue.Value;

    private static ServerManifest Load()
    {
        using var stream = typeof(ServerManifest).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");

        return JsonSerializer.Deserialize<ServerManifest>(stream, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is empty.");
    }
}

/// <summary>
/// Only what the server card reuses - its URL comes from <see cref="ServerCardOptions.PublicMcpUrl"/> instead,
/// so each environment advertises its own endpoint.
/// </summary>
public sealed record ServerManifestRemote(string Type, IReadOnlyList<ServerCardHeader> Headers);
