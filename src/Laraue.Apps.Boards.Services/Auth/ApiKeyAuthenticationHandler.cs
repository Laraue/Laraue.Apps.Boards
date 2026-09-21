using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.Services.Auth;

/// <summary>
/// Authenticates a request via a long-lived API key (<c>X-Api-Key</c> header) instead of a JWT -
/// see <see cref="ICoreApiKeysService.ValidateAsync"/>. On success, builds a <see cref="ClaimsPrincipal"/>
/// with the exact same claim types (<c>orgId</c>, <c>id</c>) <see cref="Laraue.Apps.Boards.Common.ClaimsPrincipalExtensions.GetOrganizationAuthData"/>
/// already reads off a JWT, so every existing permission check works unchanged regardless of which
/// scheme authenticated the caller.
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ICoreApiKeysService coreApiKeysService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var headerValues))
            return AuthenticateResult.NoResult();

        var rawKey = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(rawKey))
            return AuthenticateResult.Fail($"Empty '{HeaderName}' header.");

        var principal = await coreApiKeysService.ValidateAsync(rawKey, Context.RequestAborted);
        if (principal is null)
            return AuthenticateResult.Fail("Invalid or revoked API key.");

        var identity = new ClaimsIdentity(
            [
                new Claim("orgId", principal.OrganizationId.ToString()),
                new Claim("id", principal.UserId.ToString()),
            ],
            Scheme.Name);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
