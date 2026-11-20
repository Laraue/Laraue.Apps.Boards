using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.Services.Auth;

/// <summary>
/// Authenticates a request via a long-lived API key instead of a JWT - see
/// <see cref="ICoreApiKeysService.ValidateAsync"/>. The key comes in the <c>X-Api-Key</c> header (keys made by hand,
/// agent sign-in) or as <c>Authorization: Bearer</c> (the access token an OAuth client got, BRD-362 - the same key). On success, builds a <see cref="ClaimsPrincipal"/>
/// with the exact same claim types (<c>orgId</c>, <c>id</c>) <see cref="Laraue.Apps.Boards.Common.ClaimsPrincipalExtensions.GetOrganizationAuthData"/>
/// already reads off a JWT, so every existing permission check works unchanged regardless of which
/// scheme authenticated the caller. An extra <c>apiKeyId</c> claim (absent from a JWT-issued
/// principal) lets that same extension surface which key made the request, for attribution on
/// the change history it writes.
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ICoreApiKeysService coreApiKeysService,
    ICoreUserLastSeenService lastSeenService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string HeaderName = "X-Api-Key";
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string rawKey;
        if (Request.Headers.TryGetValue(HeaderName, out var headerValues))
        {
            rawKey = headerValues.ToString();
        }
        else if (Request.Headers.Authorization.ToString() is var authorization
            && authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            rawKey = authorization[BearerPrefix.Length..].Trim();
        }
        else
        {
            return AuthenticateResult.NoResult();
        }

        if (string.IsNullOrWhiteSpace(rawKey))
            return AuthenticateResult.Fail("Empty API key.");

        var principal = await coreApiKeysService.ValidateAsync(rawKey, Context.RequestAborted);
        if (principal is null)
            return AuthenticateResult.Fail("Invalid or revoked API key.");

        await lastSeenService.Touch(principal.UserId, Context.RequestAborted);

        var identity = new ClaimsIdentity(
            [
                new Claim("orgId", principal.OrganizationId.ToString()),
                new Claim("id", principal.UserId.ToString()),
                new Claim("apiKeyId", principal.ApiKeyId.ToString()),
            ],
            Scheme.Name);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
