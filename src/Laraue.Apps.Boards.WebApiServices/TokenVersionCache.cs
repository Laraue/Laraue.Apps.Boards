using Microsoft.Extensions.Caching.Memory;

namespace Laraue.Apps.Boards.WebApiServices;

/// <summary>
/// Caches users' current <c>User.TokenVersion</c> for <see cref="TokenVersionService"/>, so checking a
/// token isn't a query per request. Behind an interface so the in-process cache can be replaced by a
/// shared one (e.g. Redis) - then a bump made by one host applies on every host at once.
/// </summary>
public interface ITokenVersionCache
{
    /// <summary>
    /// The user's cached version, or null when it isn't cached.
    /// </summary>
    Task<int?> Get(Guid userId, CancellationToken cancellationToken);

    Task Set(Guid userId, int tokenVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Drops the user's cached version - call it once a transaction that bumped the version commits.
    /// </summary>
    Task Remove(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Per-host cache: a bump applies at once on the host that removes the entry, and on other hosts once
/// their entry expires (<see cref="Duration"/>).
/// </summary>
public class MemoryTokenVersionCache(IMemoryCache cache) : ITokenVersionCache
{
    /// <summary>
    /// The longest a revoked token keeps working on a host that didn't make the bump itself.
    /// </summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(30);

    public Task<int?> Get(Guid userId, CancellationToken cancellationToken)
    {
        return Task.FromResult(cache.TryGetValue(CacheKey(userId), out int tokenVersion) ? tokenVersion : (int?)null);
    }

    public Task Set(Guid userId, int tokenVersion, CancellationToken cancellationToken)
    {
        cache.Set(CacheKey(userId), tokenVersion, Duration);
        return Task.CompletedTask;
    }

    public Task Remove(Guid userId, CancellationToken cancellationToken)
    {
        cache.Remove(CacheKey(userId));
        return Task.CompletedTask;
    }

    private static string CacheKey(Guid userId) => $"token-version:{userId}";
}
