using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Records that a user used Boards (<see cref="User.LastSeenAt"/>, BRD-348). Called on every authenticated web
/// request (<c>TokenVersionValidation</c>), Telegram update (<c>TouchUserMiddleware</c>) and MCP call
/// (<see cref="Auth.ApiKeyAuthenticationHandler"/>), so it must stay cheap: it writes at most once per
/// <see cref="CoreUserLastSeenService.WriteInterval"/> per user and never fails the caller.
/// </summary>
public interface ICoreUserLastSeenService
{
    Task Touch(Guid userId, CancellationToken cancellationToken);
}

public sealed class CoreUserLastSeenService(
    DatabaseContext context,
    IMemoryCache cache,
    IDateTimeProvider dateTimeProvider,
    ILogger<CoreUserLastSeenService> logger)
    : ICoreUserLastSeenService
{
    /// <summary>
    /// How far behind <see cref="User.LastSeenAt"/> may be. Daily and weekly metrics don't need more precision.
    /// </summary>
    public static readonly TimeSpan WriteInterval = TimeSpan.FromHours(1);

    public async Task Touch(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            return;

        // The cache saves the query on this host; the condition in the update keeps several hosts from
        // writing the same hour twice.
        var cacheKey = $"last-seen:{userId}";
        if (cache.TryGetValue(cacheKey, out _))
            return;

        var now = dateTimeProvider.UtcNow;
        var writtenBefore = now - WriteInterval;

        try
        {
            await context.Users
                .Where(x => x.Id == userId)
                .Where(x => x.LastSeenAt == null || x.LastSeenAt < writtenBefore)
                .ExecuteUpdateAsync(
                    u => u.SetProperty(x => x.LastSeenAt, now),
                    cancellationToken);

            cache.Set(cacheKey, true, WriteInterval);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Activity tracking must never fail the request it rides on; the next request tries again.
            logger.LogWarning(ex, "Updating the last seen time of user {UserId} failed", userId);
        }
    }
}
