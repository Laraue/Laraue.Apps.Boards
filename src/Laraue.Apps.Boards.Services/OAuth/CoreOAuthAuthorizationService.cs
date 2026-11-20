using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.OAuth;

/// <summary>
/// OAuth authorization code + PKCE for MCP clients (<see cref="OAuthAuthorizationRequest"/>, BRD-362): the authorize
/// endpoint starts a request, a person allows or denies it on the consent page, and the client exchanges the code for
/// its token - a new <see cref="ApiKey"/>. Every state change is one conditional <c>UPDATE ... WHERE State =
/// expected</c>, like <see cref="CoreAgentSignInService"/>, so a request is decided once and its code redeemed once.
/// The protocol checks of the authorize request (client document, redirect URI, PKCE method, resource) are the
/// caller's; this service checks what only the stored request can tell - the code, its client, redirect URI, resource
/// and PKCE verifier. No permission checks, like every core service: the caller checks that the person may allow it
/// for the organization.
/// </summary>
public interface ICoreOAuthAuthorizationService
{
    /// <summary>
    /// Saves a pending request and returns its id, which the consent page finds it by.
    /// </summary>
    Task<Guid> Start(
        OAuthAuthorizationStart start,
        CancellationToken cancellationToken);

    /// <summary>
    /// Pending -> Approved for the user and organization. Returns the authorization code to send the client, or null
    /// when no pending, unexpired request has this id.
    /// </summary>
    Task<string?> Approve(
        Guid requestId,
        Guid userId,
        long organizationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Pending -> Denied. False when no pending, unexpired request has this id.
    /// </summary>
    Task<bool> Deny(
        Guid requestId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The token request: exchanges the code for a new API key, revoking the key this client got before for the same
    /// person and organization. A code sent again after it was redeemed revokes the key it was redeemed for (RFC 6749
    /// 4.1.2: someone else may have it). The caller must have opened a transaction, and commits it whatever the
    /// outcome - an <see cref="OAuthTokenOutcome.InvalidGrant"/> may have revoked a key.
    /// </summary>
    Task<OAuthTokenResult> Redeem(
        OAuthTokenRequest request,
        CancellationToken cancellationToken);
}

/// <param name="ClientId">The URL of the client's metadata document.</param>
/// <param name="ClientName">The document's <c>client_name</c>, cut to fit the column.</param>
/// <param name="RedirectUri">Checked by the caller against the document's <c>redirect_uris</c>.</param>
/// <param name="ClientState">The client's <c>state</c>, returned to it unchanged.</param>
/// <param name="CodeChallenge">The PKCE S256 challenge.</param>
/// <param name="Resource">The MCP server's URL the token is asked for.</param>
public sealed record OAuthAuthorizationStart(
    string ClientId,
    string ClientName,
    string RedirectUri,
    string? ClientState,
    string CodeChallenge,
    string? Resource);

/// <param name="Resource">Optional in the token request; when sent, it must be the one the code was asked for.</param>
public sealed record OAuthTokenRequest(
    string Code,
    string ClientId,
    string RedirectUri,
    string CodeVerifier,
    string? Resource);

/// <param name="ApiKey">Only for <see cref="OAuthTokenOutcome.Issued"/> - the client's access token.</param>
public sealed record OAuthTokenResult(
    OAuthTokenOutcome Outcome,
    ApiKeyCreationResult? ApiKey = null);

public enum OAuthTokenOutcome
{
    /// <summary>
    /// The code was valid: the result carries the new key.
    /// </summary>
    Issued,

    /// <summary>
    /// OAuth's <c>invalid_grant</c>: the code is unknown, expired, already used, not approved, or was issued to
    /// another client, redirect URI or resource, or the PKCE verifier doesn't match. Which one isn't told, like
    /// OAuth itself.
    /// </summary>
    InvalidGrant,
}

public class CoreOAuthAuthorizationService(
    DatabaseContext context,
    ICoreApiKeysService coreApiKeysService,
    IDateTimeProvider dateTimeProvider)
    : ICoreOAuthAuthorizationService
{
    /// <summary>
    /// How long the person has to answer and the client to redeem the code after that.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public async Task<Guid> Start(
        OAuthAuthorizationStart start,
        CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;

        var request = new OAuthAuthorizationRequest
        {
            Id = Guid.CreateVersion7(),
            ClientId = start.ClientId,
            ClientName = Cut(start.ClientName, Constraints.MaxOAuthClientNameLength),
            RedirectUri = start.RedirectUri,
            ClientState = start.ClientState,
            CodeChallenge = start.CodeChallenge,
            Resource = start.Resource,
            State = OAuthAuthorizationState.Pending,
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };

        context.OAuthAuthorizationRequests.Add(request);
        await context.SaveChangesAsync(cancellationToken);

        return request.Id;
    }

    public async Task<string?> Approve(
        Guid requestId,
        Guid userId,
        long organizationId,
        CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;
        var code = StringGenerator.GenerateOAuthAuthorizationCode();
        var codeHash = SecretHash.Compute(code);

        var updatedCount = await PendingRequest(requestId, now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.State, OAuthAuthorizationState.Approved)
                    .SetProperty(x => x.CodeHash, codeHash)
                    .SetProperty(x => x.UserId, userId)
                    .SetProperty(x => x.OrganizationId, organizationId)
                    .SetProperty(x => x.DecidedAt, now),
                cancellationToken);

        return updatedCount > 0 ? code : null;
    }

    public async Task<bool> Deny(
        Guid requestId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;

        var updatedCount = await PendingRequest(requestId, now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.State, OAuthAuthorizationState.Denied)
                    .SetProperty(x => x.UserId, userId)
                    .SetProperty(x => x.DecidedAt, now),
                cancellationToken);

        return updatedCount > 0;
    }

    public async Task<OAuthTokenResult> Redeem(
        OAuthTokenRequest tokenRequest,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var codeHash = SecretHash.Compute(tokenRequest.Code);

        var request = await context.OAuthAuthorizationRequests
            .Where(x => x.CodeHash == codeHash)
            .Select(x => new
            {
                x.Id,
                x.State,
                x.ExpiresAt,
                x.ClientId,
                x.ClientName,
                x.RedirectUri,
                x.CodeChallenge,
                x.Resource,
                x.UserId,
                x.OrganizationId,
                x.ApiKeyId,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (request is null)
            return InvalidGrant;

        var organizationId = request.OrganizationId!.Value;
        var userId = request.UserId!.Value;

        if (request.State == OAuthAuthorizationState.Completed)
        {
            if (request.ApiKeyId is { } issuedKeyId)
            {
                await coreApiKeysService.RevokeAsync(
                    organizationId,
                    userId,
                    issuedKeyId,
                    cancellationToken);
            }

            return InvalidGrant;
        }

        var now = dateTimeProvider.UtcNow;

        if (request.State != OAuthAuthorizationState.Approved
            || request.ExpiresAt <= now
            || request.ClientId != tokenRequest.ClientId
            || request.RedirectUri != tokenRequest.RedirectUri
            || (tokenRequest.Resource is not null && request.Resource != tokenRequest.Resource)
            || !IsVerifierValid(tokenRequest.CodeVerifier, request.CodeChallenge))
        {
            return InvalidGrant;
        }

        // Redeem it once - a concurrent token request that loses this update sees the code as used up.
        var completedCount = await context.OAuthAuthorizationRequests
            .Where(x => x.Id == request.Id && x.State == OAuthAuthorizationState.Approved)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.State, OAuthAuthorizationState.Completed)
                    .SetProperty(x => x.CompletedAt, now),
                cancellationToken);

        if (completedCount == 0)
            return InvalidGrant;

        var previousKeyIds = await context.OAuthAuthorizationRequests
            .Where(x => x.OrganizationId == organizationId
                && x.UserId == userId
                && x.ClientId == request.ClientId
                && x.ApiKeyId != null
                && x.ApiKey!.RevokedAt == null)
            .Select(x => x.ApiKeyId!.Value)
            .ToListAsync(cancellationToken);

        // RevokeAsync only touches the user's own key in this organization.
        foreach (var previousKeyId in previousKeyIds)
        {
            await coreApiKeysService.RevokeAsync(
                organizationId,
                userId,
                previousKeyId,
                cancellationToken);
        }

        var apiKey = await coreApiKeysService.CreateAsync(
            organizationId,
            userId,
            KeyName(request.ClientName, request.ClientId),
            cancellationToken);

        await context.OAuthAuthorizationRequests
            .Where(x => x.Id == request.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ApiKeyId, apiKey.Id),
                cancellationToken);

        return new OAuthTokenResult(OAuthTokenOutcome.Issued, apiKey);
    }

    /// <summary>
    /// The issued key's name - what the key list and issue history show, e.g. "ChatGPT (chatgpt.com)": the client's
    /// own name and the host of its <c>client_id</c>, which the name can't fake. Cut to fit the key name's column,
    /// keeping the host when the name is long.
    /// </summary>
    public static string KeyName(string clientName, string clientId)
    {
        const int maxLength = 64;

        var host = Uri.TryCreate(clientId, UriKind.Absolute, out var uri) ? uri.Host : clientId;
        var suffix = Cut($" ({host})", maxLength / 2);

        return Cut(clientName, maxLength - suffix.Length) + suffix;
    }

    /// <summary>
    /// PKCE S256 (RFC 7636 4.6): BASE64URL(SHA256(ASCII(code_verifier))) must equal the challenge. A verifier is 43-128
    /// characters of [A-Z a-z 0-9 - . _ ~].
    /// </summary>
    public static bool IsVerifierValid(string codeVerifier, string codeChallenge)
    {
        if (codeVerifier.Length is < 43 or > 128 || !codeVerifier.All(IsUnreserved))
            return false;

        var computed = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed),
            Encoding.ASCII.GetBytes(codeChallenge));
    }

    private static bool IsUnreserved(char c)
    {
        return char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~';
    }

    private static readonly OAuthTokenResult InvalidGrant = new(OAuthTokenOutcome.InvalidGrant);

    private static string Cut(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private IQueryable<OAuthAuthorizationRequest> PendingRequest(Guid requestId, DateTime now)
    {
        return context.OAuthAuthorizationRequests
            .Where(x => x.Id == requestId
                && x.State == OAuthAuthorizationState.Pending
                && x.ExpiresAt > now);
    }
}
