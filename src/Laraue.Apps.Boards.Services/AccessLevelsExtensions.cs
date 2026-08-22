using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Chainable guard clauses for the "resolve AccessLevels, throw if missing/not permitted" pattern
/// repeated across callers of <see cref="IAccessService"/>. The throw (and its message) stays at
/// the call site - these just remove the boilerplate null/flag check around it. Chain directly off
/// the IAccessService call, e.g.
/// <c>await accessService.GetAccessLevelsByEpicId(...).OrThrowNotFound(msg).EnsureOrThrowForbidden(p, msg)</c>.
/// </summary>
public static class AccessLevelsExtensions
{
    /// <summary>
    /// Throws <see cref="NotFoundException"/> with <paramref name="message"/> when the entity
    /// wasn't resolved (not found, or not readable by the user).
    /// </summary>
    public static async Task<AccessLevels> OrThrowNotFound(this Task<AccessLevels?> accessLevelsTask, string message)
    {
        var accessLevels = await accessLevelsTask;
        return accessLevels ?? throw new NotFoundException(message);
    }

    /// <summary>
    /// Throws <see cref="ForbiddenException"/> with <paramref name="message"/> when
    /// <paramref name="permission"/> doesn't hold.
    /// </summary>
    public static async Task<AccessLevels> EnsureOrThrowForbidden(
        this Task<AccessLevels> accessLevelsTask,
        Func<AccessLevels, bool> permission,
        string message)
    {
        var accessLevels = await accessLevelsTask;

        if (!permission(accessLevels))
            throw new ForbiddenException(message);

        return accessLevels;
    }

    /// <summary>
    /// Throws <see cref="NotFoundException"/> with <paramref name="message"/> when
    /// <paramref name="permission"/> doesn't hold. Use when the caller intentionally reports a
    /// missing permission as "not found" rather than "forbidden".
    /// </summary>
    public static async Task<AccessLevels> EnsureOrThrowNotFound(
        this Task<AccessLevels> accessLevelsTask,
        Func<AccessLevels, bool> permission,
        string message)
    {
        var accessLevels = await accessLevelsTask;

        if (!permission(accessLevels))
            throw new NotFoundException(message);

        return accessLevels;
    }
}
