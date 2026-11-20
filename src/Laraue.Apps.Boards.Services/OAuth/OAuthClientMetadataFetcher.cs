using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.Services.OAuth;

/// <summary>
/// Fetches an MCP client's Client ID Metadata Document - its <c>client_id</c> is the document's URL (BRD-362). Only
/// the URLs listed in <see cref="OAuthClientsOptions.AllowedClientIds"/> are fetched, so the authorize request can't
/// make Boards request an address of the caller's choice. Usable documents are cached, by their <c>Cache-Control</c>
/// within limits.
/// </summary>
public interface IOAuthClientMetadataFetcher
{
    Task<OAuthClientMetadataResult> Fetch(string clientId, CancellationToken cancellationToken);
}

/// <summary>
/// Which MCP clients may sign in with OAuth - config section <c>OAuth</c>.
/// </summary>
public sealed class OAuthClientsOptions
{
    /// <summary>
    /// The clients' <c>client_id</c>s - the exact URLs of their metadata documents, e.g.
    /// <c>https://chatgpt.com/oauth/client.json</c>. A client not listed here can't start a sign-in.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string[] AllowedClientIds { get; set; } = [];
}

/// <param name="httpClient">
/// A typed client, set up by <see cref="ConfigureClient"/>, that follows no redirects.
/// </param>
public class OAuthClientMetadataFetcher(
    HttpClient httpClient,
    IMemoryCache memoryCache,
    IOptions<OAuthClientsOptions> options,
    ILogger<OAuthClientMetadataFetcher> logger)
    : IOAuthClientMetadataFetcher
{
    /// <summary>
    /// The CIMD draft's suggested limit - a document holds a name and a few URLs.
    /// </summary>
    public const int MaxDocumentBytes = 5 * 1024;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The typed client's limits: <see cref="Timeout"/> for the whole request and <see cref="MaxDocumentBytes"/> for
    /// the body.
    /// </summary>
    public static void ConfigureClient(HttpClient client)
    {
        client.Timeout = Timeout;
        client.MaxResponseContentBufferSize = MaxDocumentBytes;
    }

    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinCacheDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxCacheDuration = TimeSpan.FromHours(1);

    public async Task<OAuthClientMetadataResult> Fetch(string clientId, CancellationToken cancellationToken)
    {
        if (!options.Value.AllowedClientIds.Contains(clientId, StringComparer.Ordinal))
            return OAuthClientMetadataResult.Fail(OAuthClientMetadataError.UnknownClient);

        var cacheKey = $"oauth-client-metadata:{clientId}";
        if (memoryCache.TryGetValue(cacheKey, out OAuthClientMetadata? cached) && cached is not null)
            return OAuthClientMetadataResult.Ok(cached);

        byte[] document;
        TimeSpan cacheDuration;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, clientId);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // The whole body is read before SendAsync returns, so the client's timeout covers it too, and a body over
            // its MaxResponseContentBufferSize throws HttpRequestException.
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                logger.LogWarning(
                    "Client metadata document {ClientId} answered {StatusCode}",
                    clientId,
                    (int)response.StatusCode);
                return OAuthClientMetadataResult.Fail(OAuthClientMetadataError.Unreachable);
            }

            document = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            cacheDuration = CacheDuration(response.Headers.CacheControl);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Client metadata document {ClientId} couldn't be fetched", clientId);
            return OAuthClientMetadataResult.Fail(OAuthClientMetadataError.Unreachable);
        }

        var metadata = Parse(clientId, document);
        if (metadata is null)
        {
            logger.LogWarning("Client metadata document {ClientId} is invalid", clientId);
            return OAuthClientMetadataResult.Fail(OAuthClientMetadataError.InvalidDocument);
        }

        if (cacheDuration > TimeSpan.Zero)
            memoryCache.Set(cacheKey, metadata, cacheDuration);

        return OAuthClientMetadataResult.Ok(metadata);
    }

    /// <summary>
    /// The document's fields, or null when it can't be used - see <see cref="OAuthClientMetadataError.InvalidDocument"/>.
    /// </summary>
    public static OAuthClientMetadata? Parse(string clientId, ReadOnlySpan<byte> document)
    {
        ClientMetadataDocument? parsed;
        try
        {
            // Wrong types (a number for client_name, an object for redirect_uris, an array as the root) throw too.
            parsed = JsonSerializer.Deserialize<ClientMetadataDocument>(document, DocumentJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        // token_endpoint_auth_method isn't checked: ChatGPT's document says private_key_jwt and lists none among the
        // methods it supports. Boards' metadata offers only none, and the token request relies on PKCE whatever the
        // client sends.
        if (parsed?.ClientId != clientId
            || parsed.RedirectUris is not { Length: > 0 } redirectUris
            // The redirect URIs aren't checked further: the document comes from a listed client's own host, and
            // the authorize request's redirect_uri must match one of them (OAuthClientMetadata.AllowsRedirectUri).
            || redirectUris.Any(uri => string.IsNullOrEmpty(uri)))
        {
            return null;
        }

        var clientName = string.IsNullOrWhiteSpace(parsed.ClientName)
            ? new Uri(clientId).Host
            : parsed.ClientName.Trim();

        return new OAuthClientMetadata(clientId, clientName, redirectUris!);
    }

    private static readonly JsonSerializerOptions DocumentJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// The fields of a client metadata document Boards reads; the others are ignored.
    /// </summary>
    private sealed record ClientMetadataDocument(
        string? ClientId,
        string? ClientName,
        string?[]? RedirectUris);

    private static TimeSpan CacheDuration(CacheControlHeaderValue? cacheControl)
    {
        if (cacheControl is null)
            return DefaultCacheDuration;

        if (cacheControl.NoStore || cacheControl.NoCache)
            return TimeSpan.Zero;

        if (cacheControl.MaxAge is not { } maxAge)
            return DefaultCacheDuration;

        if (maxAge <= TimeSpan.Zero)
            return TimeSpan.Zero;

        return maxAge < MinCacheDuration
            ? MinCacheDuration
            : maxAge > MaxCacheDuration ? MaxCacheDuration : maxAge;
    }
}
