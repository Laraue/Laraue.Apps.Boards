using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices;
using Laraue.Telegram.NET.Abstractions;

namespace Laraue.Apps.Boards.TelegramHost;

/// <summary>
/// Records the sender of every Telegram update as seen (<see cref="ICoreUserLastSeenService"/>, BRD-348). The
/// user is known only once the authentication middleware, further down the pipeline, has run, so the touch
/// happens on the way back - also when a handler failed, since the user still used the bot.
/// </summary>
public class TouchUserMiddleware(
    RequestContext context,
    ICoreUserLastSeenService lastSeenService)
    : ITelegramMiddleware
{
    public async Task InvokeAsync(Func<CancellationToken, Task> next, CancellationToken ct)
    {
        try
        {
            await next(ct);
        }
        finally
        {
            await lastSeenService.Touch(context.UserId, ct);
        }
    }
}
