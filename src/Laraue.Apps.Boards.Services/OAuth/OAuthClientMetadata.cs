using System.Net;

namespace Laraue.Apps.Boards.Services.OAuth;

/// <summary>
/// What an MCP client says about itself in its Client ID Metadata Document - the JSON file its <c>client_id</c> URL
/// points to (BRD-362). Only the fields Boards uses.
/// </summary>
/// <param name="ClientId">The document's URL, equal to the <c>client_id</c> inside it.</param>
/// <param name="ClientName">The document's <c>client_name</c>, or the URL's host when it has none.</param>
/// <param name="RedirectUris">Where the client may get the browser back to.</param>
public sealed record OAuthClientMetadata(
    string ClientId,
    string ClientName,
    IReadOnlyList<string> RedirectUris)
{
    /// <summary>
    /// The host of <see cref="ClientId"/> - who actually serves the document, whatever <see cref="ClientName"/> says.
    /// </summary>
    public string Host => new Uri(ClientId).Host;

    /// <summary>
    /// Whether the authorize request's <c>redirect_uri</c> is one of <see cref="RedirectUris"/>: compared exactly,
    /// except that a loopback one (<c>http://localhost</c>, <c>127.0.0.1</c>, <c>[::1]</c>) may use any port, since a
    /// native client listens on a port it gets at run time (RFC 8252 7.3).
    /// </summary>
    public bool AllowsRedirectUri(string redirectUri)
    {
        if (RedirectUris.Contains(redirectUri, StringComparer.Ordinal))
            return true;

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var requested) || !IsLoopbackHttp(requested))
            return false;

        return RedirectUris.Any(registered =>
            Uri.TryCreate(registered, UriKind.Absolute, out var allowed)
            && IsLoopbackHttp(allowed)
            && string.Equals(allowed.Host, requested.Host, StringComparison.OrdinalIgnoreCase)
            && allowed.PathAndQuery == requested.PathAndQuery);
    }

    private static bool IsLoopbackHttp(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttp
            && (uri.IsLoopback || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip)));
    }
}

/// <summary>
/// Why a client's metadata document can't be used. The authorize endpoint shows it on its error page - never as a
/// redirect, since the redirect URI isn't trusted yet.
/// </summary>
public enum OAuthClientMetadataError
{
    /// <summary>
    /// The <c>client_id</c> isn't an HTTPS URL with a path on the default port, or has a fragment, credentials or dot
    /// segments.
    /// </summary>
    InvalidClientId,

    /// <summary>
    /// The document couldn't be fetched: the host resolves to a private address, didn't answer in time, answered with
    /// something other than 200, or sent more than the size limit.
    /// </summary>
    Unreachable,

    /// <summary>
    /// The document isn't a JSON object, its <c>client_id</c> isn't its own URL, it has no usable
    /// <c>redirect_uris</c>, or it asks for a token endpoint authentication other than <c>none</c>.
    /// </summary>
    InvalidDocument,
}

/// <param name="Metadata">Set when the document could be used.</param>
/// <param name="Error">Set when it couldn't.</param>
public sealed record OAuthClientMetadataResult(
    OAuthClientMetadata? Metadata,
    OAuthClientMetadataError? Error)
{
    public static OAuthClientMetadataResult Ok(OAuthClientMetadata metadata) => new(metadata, null);

    public static OAuthClientMetadataResult Fail(OAuthClientMetadataError error) => new(null, error);
}
