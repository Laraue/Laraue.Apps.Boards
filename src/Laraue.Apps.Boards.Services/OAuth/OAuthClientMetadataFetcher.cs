using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.Boards.Services.OAuth;

/// <summary>
/// Fetches an MCP client's Client ID Metadata Document - its <c>client_id</c> is the document's URL (BRD-362). The URL
/// comes from whoever starts the authorize request, so fetching it must not reach anything inside our network: see
/// <see cref="CreateHandler"/>. Usable documents are cached, by their <c>Cache-Control</c> within limits.
/// </summary>
public interface IOAuthClientMetadataFetcher
{
    Task<OAuthClientMetadataResult> Fetch(string clientId, CancellationToken cancellationToken);
}

public class OAuthClientMetadataFetcher(
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    ILogger<OAuthClientMetadataFetcher> logger)
    : IOAuthClientMetadataFetcher
{
    public const string HttpClientName = "OAuthClientMetadata";

    /// <summary>
    /// The CIMD draft's suggested limit - a document holds a name and a few URLs.
    /// </summary>
    public const int MaxDocumentBytes = 5 * 1024;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinCacheDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxCacheDuration = TimeSpan.FromHours(1);

    private static readonly string[] ForbiddenRedirectSchemes = ["javascript", "data", "file", "vbscript", "blob"];

    public async Task<OAuthClientMetadataResult> Fetch(string clientId, CancellationToken cancellationToken)
    {
        if (!IsValidClientId(clientId))
            return OAuthClientMetadataResult.Fail(OAuthClientMetadataError.InvalidClientId);

        var cacheKey = $"oauth-client-metadata:{clientId}";
        if (memoryCache.TryGetValue(cacheKey, out OAuthClientMetadata? cached) && cached is not null)
            return OAuthClientMetadataResult.Ok(cached);

        byte[] document;
        TimeSpan cacheDuration;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, clientId);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                logger.LogInformation(
                    "Client metadata document {ClientId} answered {StatusCode}",
                    clientId,
                    (int)response.StatusCode);
                return OAuthClientMetadataResult.Fail(OAuthClientMetadataError.Unreachable);
            }

            var read = await ReadLimited(response.Content, timeout.Token);
            if (read is null)
            {
                logger.LogInformation("Client metadata document {ClientId} is over {Limit} bytes", clientId, MaxDocumentBytes);
                return OAuthClientMetadataResult.Fail(OAuthClientMetadataError.Unreachable);
            }

            document = read;
            cacheDuration = CacheDuration(response.Headers.CacheControl);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or SocketException
            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(exception, "Client metadata document {ClientId} couldn't be fetched", clientId);
            return OAuthClientMetadataResult.Fail(OAuthClientMetadataError.Unreachable);
        }

        var metadata = Parse(clientId, document);
        if (metadata is null)
        {
            logger.LogInformation("Client metadata document {ClientId} is invalid", clientId);
            return OAuthClientMetadataResult.Fail(OAuthClientMetadataError.InvalidDocument);
        }

        if (cacheDuration > TimeSpan.Zero)
            memoryCache.Set(cacheKey, metadata, cacheDuration);

        return OAuthClientMetadataResult.Ok(metadata);
    }

    /// <summary>
    /// The CIMD draft's rules for a <c>client_id</c> URL: HTTPS, a path, no fragment, credentials or dot segments.
    /// Also the default port only and no IP literal, so a client id can't name an arbitrary port or address.
    /// </summary>
    public static bool IsValidClientId(string clientId)
    {
        if (clientId.Length > DataAccess.Constraints.MaxOAuthUrlLength
            || !Uri.TryCreate(clientId, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps
            && uri.IsDefaultPort
            && uri.HostNameType == UriHostNameType.Dns
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Fragment)
            && !clientId.Contains('#')
            && uri.AbsolutePath.Length > 1
            && !HasDotSegment(clientId)
            // Uri normalizes what it parses; a client id must already be in that form, since the document's
            // client_id is compared with it as a string.
            && uri.AbsoluteUri == clientId;
    }

    /// <summary>
    /// The document's fields, or null when it can't be used - see <see cref="OAuthClientMetadataError.InvalidDocument"/>.
    /// </summary>
    public static OAuthClientMetadata? Parse(string clientId, ReadOnlySpan<byte> document)
    {
        JsonDocument json;
        try
        {
            var reader = new Utf8JsonReader(document);
            if (!JsonDocument.TryParseValue(ref reader, out var parsed))
                return null;
            json = parsed;
        }
        catch (JsonException)
        {
            return null;
        }

        using (json)
        {
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!root.TryGetProperty("client_id", out var documentClientId)
                || documentClientId.ValueKind != JsonValueKind.String
                || documentClientId.GetString() != clientId)
            {
                return null;
            }

            // Boards has no client secrets: only public clients, which prove themselves with PKCE.
            if (root.TryGetProperty("token_endpoint_auth_method", out var authMethod)
                && (authMethod.ValueKind != JsonValueKind.String || authMethod.GetString() != "none"))
            {
                return null;
            }

            if (!root.TryGetProperty("redirect_uris", out var redirectUrisElement)
                || redirectUrisElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var redirectUris = new List<string>();
            foreach (var element in redirectUrisElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String || !IsUsableRedirectUri(element.GetString()!))
                    return null;
                redirectUris.Add(element.GetString()!);
            }

            if (redirectUris.Count == 0)
                return null;

            var host = new Uri(clientId).Host;
            var clientName = root.TryGetProperty("client_name", out var nameElement)
                && nameElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(nameElement.GetString())
                    ? nameElement.GetString()!.Trim()
                    : host;

            return new OAuthClientMetadata(clientId, clientName, redirectUris);
        }
    }

    /// <summary>
    /// A redirect URI Boards will send a code to: absolute, no fragment, HTTPS or loopback HTTP, or a native app's
    /// own scheme - never one that runs or reads something in the browser.
    /// </summary>
    private static bool IsUsableRedirectUri(string value)
    {
        if (value.Length > DataAccess.Constraints.MaxOAuthUrlLength
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
            return true;

        if (uri.Scheme == Uri.UriSchemeHttp)
            return uri.IsLoopback;

        return !ForbiddenRedirectSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The handler for <see cref="HttpClientName"/>: connects only to public addresses, checked on the address it
    /// actually connects to (so a DNS answer can't change between the check and the connection), follows no
    /// redirects and uses no proxy.
    /// </summary>
    public static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            ConnectTimeout = Timeout,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);

                // A name resolving to any private address is refused whole, rather than trying its public ones.
                if (addresses.Length == 0 || !addresses.All(IsPublicAddress))
                {
                    throw new HttpRequestException(
                        $"{context.DnsEndPoint.Host} doesn't resolve to public addresses only.");
                }

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(addresses[0], context.DnsEndPoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
    }

    /// <summary>
    /// Whether an address is on the public internet - not loopback, private, link-local, carrier-grade NAT, multicast,
    /// reserved or documentation ranges, in IPv4 or IPv6 (an IPv4-mapped IPv6 address is checked as IPv4).
    /// </summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.Broadcast))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0
                || b[0] == 10
                || b[0] == 127
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 192 && b[1] == 0 && b[2] == 2)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                || (b[0] == 203 && b[1] == 0 && b[2] == 113)
                || b[0] >= 224);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || address.IsIPv6UniqueLocal
                || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8)
                // NAT64 (64:ff9b::/96) and 6to4 (2002::/16) carry an IPv4 address that isn't checked here.
                || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b)
                || (b[0] == 0x20 && b[1] == 0x02));
        }

        return false;
    }

    private static bool HasDotSegment(string url)
    {
        var path = url.Split('?')[0];
        return path.Split('/').Any(segment => segment is "." or ".." || segment.Equals("%2e", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("%2e%2e", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<byte[]?> ReadLimited(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxDocumentBytes)
            return null;

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaxDocumentBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length
            && (read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken)) > 0)
        {
            total += read;
        }

        return total > MaxDocumentBytes ? null : buffer[..total];
    }

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
