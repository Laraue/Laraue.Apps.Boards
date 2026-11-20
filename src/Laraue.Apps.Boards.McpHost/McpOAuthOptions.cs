using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace Laraue.Apps.Boards.McpHost;

/// <summary>
/// Where an OAuth client signs in to get a token for this server (BRD-362) - config section <c>OAuth</c>. The
/// authorization server is Boards' WebApiHost.
/// </summary>
public sealed class McpOAuthOptions
{
    /// <summary>
    /// The authorization server's issuer, e.g. <c>https://boards.laraue.com</c> - its metadata is at
    /// <c>/.well-known/oauth-authorization-server</c> there.
    /// </summary>
    [Required]
    [Url]
    public required string AuthorizationServer { get; set; }

    /// <summary>
    /// Sets up the SDK's MCP scheme from this server's public URL (<see cref="ServerCardOptions.PublicMcpUrl"/>, also
    /// the <c>resource</c> a token is issued for): RFC 9728 protected resource metadata at
    /// <c>/.well-known/oauth-protected-resource</c> followed by the URL's path - e.g.
    /// <c>https://boards.laraue.com/.well-known/oauth-protected-resource/boards-mcp/mcp</c>, which nginx passes here
    /// unchanged - and the 401's <c>WWW-Authenticate</c> pointing at it.
    /// </summary>
    public static void Configure(
        McpAuthenticationOptions options,
        IOptions<ServerCardOptions> serverCard,
        IOptions<McpOAuthOptions> oauth)
    {
        var resource = new Uri(serverCard.Value.PublicMcpUrl);

        options.ResourceMetadataUri = new Uri(
            resource,
            $"/.well-known/oauth-protected-resource{resource.AbsolutePath}");
        options.ResourceMetadata = new ProtectedResourceMetadata
        {
            Resource = resource.ToString(),
            AuthorizationServers = [oauth.Value.AuthorizationServer],
            BearerMethodsSupported = ["header"],
            ResourceName = "Laraue Boards",
        };
    }
}
