using System.Security.Claims;
using Laraue.Apps.Boards.DataAccess;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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

    /// <summary>
    /// Drops the cached version of the user, so a bump made by this host applies to its next request.
    /// Other hosts pick it up once their cache entry expires (<see cref="TokenVersionService.CacheDuration"/>).
    /// </summary>
    void Forget(Guid userId);
}

public class TokenVersionService(DatabaseContext context, IMemoryCache cache) : ITokenVersionService
{
    /// <summary>
    /// How long a user's version is cached per host - the longest a revoked token keeps working on a
    /// host that didn't make the bump itself. Keeps the check from being a query per request.
    /// </summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async Task<bool> IsCurrent(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirst("id")?.Value, out var userId))
            return false;

        var tokenVersionClaim = principal.FindFirst(AuthService.TokenVersionClaim)?.Value;
        var tokenVersion = 0;
        if (tokenVersionClaim is not null && !int.TryParse(tokenVersionClaim, out tokenVersion))
            return false;

        var currentVersion = await cache.GetOrCreateAsync(
            CacheKey(userId),
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                return context.Users
                    .Where(x => x.Id == userId)
                    .Select(x => (int?)x.TokenVersion)
                    .FirstOrDefaultAsync(cancellationToken);
            });

        return currentVersion == tokenVersion;
    }

    public void Forget(Guid userId)
    {
        cache.Remove(CacheKey(userId));
    }

    private static string CacheKey(Guid userId) => $"token-version:{userId}";
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
