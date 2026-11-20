using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.OAuth;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.WebApiServices.OAuth;

/// <summary>
/// The OAuth authorization server for MCP clients (BRD-362): metadata, the authorize request, the consent page's
/// calls and the token request, on top of <see cref="ICoreOAuthAuthorizationService"/>. Authorization code with PKCE
/// S256 for public clients identified by their Client ID Metadata Document, only those in
/// <see cref="OAuthClientsOptions.AllowedClientIds"/>. The token is an organization-scoped API key, like agent
/// sign-in's; a general sign-in server still belongs to Laraue.Apps.Identity.
/// </summary>
public interface IOAuthService
{
    /// <summary>
    /// RFC 8414 authorization server metadata.
    /// </summary>
    OAuthServerMetadata GetMetadata();

    /// <summary>
    /// The authorize request. Always returns where to send the browser: the consent page for a valid request, the
    /// consent page with an error when the client or its redirect URI can't be trusted, otherwise the client's redirect
    /// URI with an OAuth error.
    /// </summary>
    Task<string> Authorize(
        OAuthAuthorizeRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// The request for the consent page. 404 when it doesn't exist, expired or was already answered.
    /// </summary>
    Task<OAuthConsentDto> GetConsent(
        Guid requestId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Allows the request for one of the user's organizations and returns where to send the browser - the client's
    /// redirect URI with the code. 404 when the user isn't a member of the organization, or the request doesn't exist,
    /// expired or was already answered.
    /// </summary>
    Task<OAuthDecisionResponse> Approve(
        Guid requestId,
        Guid userId,
        ApproveOAuthRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Denies the request and returns where to send the browser - the client's redirect URI with
    /// <c>access_denied</c>. 404 when it doesn't exist, expired or was already answered.
    /// </summary>
    Task<OAuthDecisionResponse> Deny(
        Guid requestId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The token request. Either a token or an OAuth error, never an exception for a bad request.
    /// </summary>
    Task<OAuthTokenResponse> Token(
        OAuthTokenFormRequest request,
        CancellationToken cancellationToken);
}

public class OAuthService(
    DatabaseContext context,
    ICoreOAuthAuthorizationService coreOAuthService,
    IOAuthClientMetadataFetcher clientMetadataFetcher,
    IAccessService accessService,
    IDateTimeProvider dateTimeProvider,
    IOptions<OAuthServerOptions> serverOptions,
    IOptions<WebAppOptions> webAppOptions)
    : IOAuthService
{
    private OAuthServerOptions Options => serverOptions.Value;

    public OAuthServerMetadata GetMetadata()
    {
        return new OAuthServerMetadata
        {
            Issuer = Options.Issuer,
            AuthorizationEndpoint = $"{Options.PublicApiUrl}/api/oauth/authorize",
            TokenEndpoint = $"{Options.PublicApiUrl}/api/oauth/token",
        };
    }

    public async Task<string> Authorize(
        OAuthAuthorizeRequest request,
        CancellationToken cancellationToken)
    {
        // Until the redirect URI is known to be the client's own, errors stay on our page.
        if (string.IsNullOrEmpty(request.ClientId))
            return ConsentErrorPage(OAuthConsentError.UnknownClient);

        var client = await clientMetadataFetcher.Fetch(request.ClientId, cancellationToken);
        if (client.Metadata is not { } metadata)
        {
            return ConsentErrorPage(client.Error switch
            {
                OAuthClientMetadataError.UnknownClient => OAuthConsentError.UnknownClient,
                _ => OAuthConsentError.ClientUnavailable,
            });
        }

        if (string.IsNullOrEmpty(request.RedirectUri) || !metadata.AllowsRedirectUri(request.RedirectUri))
            return ConsentErrorPage(OAuthConsentError.InvalidRedirectUri);

        if (request.ResponseType != "code")
            return ClientRedirect(request.RedirectUri, request.State, ("error", "unsupported_response_type"));

        if (request.CodeChallengeMethod != "S256"
            || request.CodeChallenge is not { Length: 43 } codeChallenge
            || !codeChallenge.All(IsBase64UrlChar))
        {
            return ClientRedirect(request.RedirectUri, request.State, ("error", "invalid_request"));
        }

        if (request.Resource != Options.Resource)
            return ClientRedirect(request.RedirectUri, request.State, ("error", "invalid_target"));

        if (request.State is { Length: > Constraints.MaxOAuthStateLength })
            return ClientRedirect(request.RedirectUri, null, ("error", "invalid_request"));

        var requestId = await coreOAuthService.Start(
            new OAuthAuthorizationStart(
                metadata.ClientId,
                metadata.ClientName,
                request.RedirectUri,
                request.State,
                codeChallenge,
                request.Resource),
            cancellationToken);

        return QueryHelpers.AddQueryString(ConsentPageUrl, "request", requestId.ToString());
    }

    public async Task<OAuthConsentDto> GetConsent(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;

        var request = await context.OAuthAuthorizationRequests
            .Where(x => x.Id == requestId
                && x.State == OAuthAuthorizationState.Pending
                && x.ExpiresAt > now)
            .Select(x => new
            {
                x.ClientId,
                x.ClientName,
                x.ExpiresAt,
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFound(requestId);

        return new OAuthConsentDto
        {
            ClientName = request.ClientName,
            ClientHost = new Uri(request.ClientId).Host,
            KeyName = CoreOAuthAuthorizationService.KeyName(request.ClientName, request.ClientId),
            ExpiresAt = request.ExpiresAt,
        };
    }

    public async Task<OAuthDecisionResponse> Approve(
        Guid requestId,
        Guid userId,
        ApproveOAuthRequest request,
        CancellationToken cancellationToken)
    {
        if (!await accessService.IsActiveMember(userId, request.OrganizationId, cancellationToken))
        {
            throw new NotFoundException(
                string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Organization", request.OrganizationId));
        }

        var code = await coreOAuthService.Approve(requestId, userId, request.OrganizationId, cancellationToken)
            ?? throw NotFound(requestId);

        var redirect = await GetRedirect(requestId, cancellationToken);

        return new OAuthDecisionResponse
        {
            RedirectUrl = ClientRedirect(redirect.RedirectUri, redirect.ClientState, ("code", code)),
        };
    }

    public async Task<OAuthDecisionResponse> Deny(
        Guid requestId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await coreOAuthService.Deny(requestId, userId, cancellationToken))
            throw NotFound(requestId);

        var redirect = await GetRedirect(requestId, cancellationToken);

        return new OAuthDecisionResponse
        {
            RedirectUrl = ClientRedirect(redirect.RedirectUri, redirect.ClientState, ("error", "access_denied")),
        };
    }

    public async Task<OAuthTokenResponse> Token(
        OAuthTokenFormRequest request,
        CancellationToken cancellationToken)
    {
        if (request.GrantType != "authorization_code")
            return OAuthTokenResponse.Error("unsupported_grant_type");

        if (string.IsNullOrEmpty(request.Code)
            || string.IsNullOrEmpty(request.ClientId)
            || string.IsNullOrEmpty(request.RedirectUri)
            || string.IsNullOrEmpty(request.CodeVerifier))
        {
            return OAuthTokenResponse.Error("invalid_request");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var result = await coreOAuthService.Redeem(
            new OAuthTokenRequest(
                request.Code,
                request.ClientId,
                request.RedirectUri,
                request.CodeVerifier,
                request.Resource),
            cancellationToken);

        // Committed whatever the outcome: a code used twice revokes the key it issued.
        await transaction.CommitAsync(cancellationToken);

        return result.Outcome == OAuthTokenOutcome.Issued
            ? OAuthTokenResponse.Bearer(result.ApiKey!.RawKey)
            : OAuthTokenResponse.Error("invalid_grant");
    }

    private string ConsentPageUrl => $"{webAppOptions.Value.Url.TrimEnd('/')}{Options.ConsentPagePath}";

    private string ConsentErrorPage(OAuthConsentError error)
    {
        return QueryHelpers.AddQueryString(ConsentPageUrl, "error", error switch
        {
            OAuthConsentError.UnknownClient => "unknown_client",
            OAuthConsentError.ClientUnavailable => "client_unavailable",
            _ => "invalid_redirect_uri",
        });
    }

    /// <summary>
    /// The client's redirect URI with the result, its <c>state</c> and our <c>iss</c> (RFC 9207, so the client can tell
    /// which authorization server answered).
    /// </summary>
    private string ClientRedirect(string redirectUri, string? state, (string Name, string Value) result)
    {
        var parameters = new Dictionary<string, string?> { [result.Name] = result.Value };
        if (state is not null)
            parameters["state"] = state;
        parameters["iss"] = Options.Issuer;

        return QueryHelpers.AddQueryString(redirectUri, parameters);
    }

    private async Task<(string RedirectUri, string? ClientState)> GetRedirect(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var redirect = await context.OAuthAuthorizationRequests
            .Where(x => x.Id == requestId)
            .Select(x => new { x.RedirectUri, x.ClientState })
            .FirstAsync(cancellationToken);

        return (redirect.RedirectUri, redirect.ClientState);
    }

    private static bool IsBase64UrlChar(char c)
    {
        return char.IsAsciiLetterOrDigit(c) || c is '-' or '_';
    }

    private static NotFoundException NotFound(Guid requestId)
    {
        return new NotFoundException(string.Format(ErrorMessages.EntityNotFound, "OAuthRequest", requestId));
    }

    private enum OAuthConsentError
    {
        UnknownClient,
        ClientUnavailable,
        InvalidRedirectUri,
    }
}

/// <summary>
/// The authorization server's side of OAuth - config section <c>OAuth</c>, next to
/// <see cref="OAuthClientsOptions.AllowedClientIds"/>.
/// </summary>
public sealed class OAuthServerOptions
{
    /// <summary>
    /// The issuer - where <c>/.well-known/oauth-authorization-server</c> is served, e.g.
    /// <c>https://boards.laraue.com</c>.
    /// </summary>
    [Required]
    [Url]
    public required string Issuer { get; set; }

    /// <summary>
    /// This host's public address, which the endpoints in the metadata are built on, e.g.
    /// <c>https://boards.laraue.com/boards-api</c>.
    /// </summary>
    [Required]
    [Url]
    public required string PublicApiUrl { get; set; }

    /// <summary>
    /// The MCP server's URL - the only <c>resource</c> a token is issued for, e.g.
    /// <c>https://boards.laraue.com/boards-mcp/mcp</c>.
    /// </summary>
    [Required]
    [Url]
    public required string Resource { get; set; }

    /// <summary>
    /// The web app's consent page, relative to <c>AppOptions:Url</c> - laraue-boards' OAuth consent page. Opened with
    /// <c>?request=</c> and the request id, or <c>?error=</c>.
    /// </summary>
    [Required]
    public required string ConsentPagePath { get; set; }
}

/// <summary>
/// RFC 8414 metadata: only public clients with authorization code + PKCE S256, identified by their Client ID Metadata
/// Document, answered with <c>iss</c> (RFC 9207).
/// </summary>
public sealed class OAuthServerMetadata
{
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    [JsonPropertyName("authorization_endpoint")]
    public required string AuthorizationEndpoint { get; init; }

    [JsonPropertyName("token_endpoint")]
    public required string TokenEndpoint { get; init; }

    [JsonPropertyName("response_types_supported")]
    public string[] ResponseTypesSupported { get; init; } = ["code"];

    [JsonPropertyName("grant_types_supported")]
    public string[] GrantTypesSupported { get; init; } = ["authorization_code"];

    [JsonPropertyName("code_challenge_methods_supported")]
    public string[] CodeChallengeMethodsSupported { get; init; } = ["S256"];

    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public string[] TokenEndpointAuthMethodsSupported { get; init; } = ["none"];

    [JsonPropertyName("client_id_metadata_document_supported")]
    public bool ClientIdMetadataDocumentSupported { get; init; } = true;

    [JsonPropertyName("authorization_response_iss_parameter_supported")]
    public bool AuthorizationResponseIssParameterSupported { get; init; } = true;
}

/// <summary>
/// The authorize request's query parameters, all optional here so a missing one is answered in OAuth's terms. Query
/// binding has no naming policy, so each OAuth name is given.
/// </summary>
public sealed class OAuthAuthorizeRequest
{
    [FromQuery(Name = "response_type")]
    public string? ResponseType { get; init; }

    [FromQuery(Name = "client_id")]
    public string? ClientId { get; init; }

    [FromQuery(Name = "redirect_uri")]
    public string? RedirectUri { get; init; }

    [FromQuery(Name = "code_challenge")]
    public string? CodeChallenge { get; init; }

    [FromQuery(Name = "code_challenge_method")]
    public string? CodeChallengeMethod { get; init; }

    [FromQuery(Name = "state")]
    public string? State { get; init; }

    [FromQuery(Name = "resource")]
    public string? Resource { get; init; }
}

/// <summary>
/// The token request's form fields, all optional here so a missing one is answered in OAuth's terms. Form binding has
/// no naming policy, so each OAuth name is given.
/// </summary>
public sealed class OAuthTokenFormRequest
{
    [FromForm(Name = "grant_type")]
    public string? GrantType { get; init; }

    [FromForm(Name = "code")]
    public string? Code { get; init; }

    [FromForm(Name = "client_id")]
    public string? ClientId { get; init; }

    [FromForm(Name = "redirect_uri")]
    public string? RedirectUri { get; init; }

    [FromForm(Name = "code_verifier")]
    public string? CodeVerifier { get; init; }

    [FromForm(Name = "resource")]
    public string? Resource { get; init; }
}

/// <summary>
/// The token response (RFC 6749 5.1) or error (5.2). The key doesn't expire, so there's no <c>expires_in</c>, and no
/// refresh token.
/// </summary>
public sealed class OAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TokenType { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; init; }

    [JsonIgnore]
    public bool IsError => ErrorCode is not null;

    public static OAuthTokenResponse Bearer(string accessToken) => new() { AccessToken = accessToken, TokenType = "Bearer" };

    public static OAuthTokenResponse Error(string error) => new() { ErrorCode = error };
}

public sealed class OAuthConsentDto
{
    /// <summary>
    /// The client's own name, from its metadata document.
    /// </summary>
    public required string ClientName { get; init; }

    /// <summary>
    /// The host serving the client's metadata document, e.g. <c>chatgpt.com</c>.
    /// </summary>
    public required string ClientHost { get; init; }

    /// <summary>
    /// The name the API key will get, as the API keys page will show it.
    /// </summary>
    public required string KeyName { get; init; }

    public required DateTime ExpiresAt { get; init; }
}

public sealed class ApproveOAuthRequest
{
    public required long OrganizationId { get; init; }
}

public sealed class OAuthDecisionResponse
{
    /// <summary>
    /// Where the consent page sends the browser - the client's redirect URI with the code or the error.
    /// </summary>
    public required string RedirectUrl { get; init; }
}
