using System.Security.Claims;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.WebApiServices;

/// <summary>
/// Checks a user or organization token against its user's current <c>User.TokenVersion</c>
/// (BRD-222), so bumping the version ends every session issued before it.
/// </summary>
public interface ITokenVersionService
{
    /// <summary>
    /// Whether the token's <see cref="AuthService.TokenVersionClaim"/> (0 when missing - a token issued
    /// before the claim existed) is the user's current version. False for a user that doesn't exist.
    /// </summary>
    Task<bool> IsCurrent(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public class TokenVersionService(DatabaseContext context, ITokenVersionCache cache) : ITokenVersionService
{
    public async Task<bool> IsCurrent(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userId = principal.GetId();

        var tokenVersionClaim = principal.FindFirst(AuthService.TokenVersionClaim)?.Value;
        var tokenVersion = 0;
        if (tokenVersionClaim is not null && !int.TryParse(tokenVersionClaim, out tokenVersion))
            return false;

        var currentVersion = await cache.Get(userId, cancellationToken);
        if (currentVersion is null)
        {
            currentVersion = await context.Users
                .Where(x => x.Id == userId)
                .Select(x => (int?)x.TokenVersion)
                .FirstOrDefaultAsync(cancellationToken);

            // A user that doesn't exist isn't cached - only a token signed with our key can get here.
            if (currentVersion is null)
                return false;

            await cache.Set(userId, currentVersion.Value, cancellationToken);
        }

        return currentVersion == tokenVersion;
    }
}

/// <summary>
/// The <see cref="JwtBearerEvents.OnTokenValidated"/> handler every JWT scheme of the web hosts uses -
/// a token with a stale version fails authentication (401).
/// </summary>
public static class TokenVersionValidation
{
    public static async Task OnTokenValidated(TokenValidatedContext context)
    {
        var tokenVersionService = context.HttpContext.RequestServices.GetRequiredService<ITokenVersionService>();
        if (!await tokenVersionService.IsCurrent(context.Principal!, context.HttpContext.RequestAborted))
            context.Fail("The token was revoked.");
    }
}
