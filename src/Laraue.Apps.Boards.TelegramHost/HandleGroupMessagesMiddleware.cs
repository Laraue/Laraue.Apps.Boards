using Laraue.Apps.Boards.TelegramServices;
using Laraue.Apps.Boards.TelegramServices.Services.GroupChats;
using Laraue.Telegram.NET.Abstractions;
using Laraue.Telegram.NET.Core.Extensions;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.TelegramHost;

/// <summary>
/// Handles messages in group/supergroup chats that weren't matched by an explicit route
/// (e.g. not a /link or /unlink command): detects bot @mentions and turns replied-to
/// messages into cards. Runs after <see cref="HandlePrivateMessagesMiddleware"/> so it can
/// claim the route for group-chat updates before that middleware's "save as a personal
/// card" fallback (meant for DMs) ever sees them.
/// </summary>
public class HandleGroupMessageMiddleware(
    RequestContext context,
    IGroupChatService groupChatService)
    : ITelegramMiddleware
{
    private static readonly UpdateType[] AllowedUpdates =
    [
        UpdateType.Message,
        UpdateType.EditedMessage,
    ];

    public async Task InvokeAsync(Func<CancellationToken, Task> next, CancellationToken ct)
    {
        await next(ct);

        if (context.GetExecutedRoute() is not null || !AllowedUpdates.Contains(context.Update.Type))
            return;

        var message = context.Update.Message ?? context.Update.EditedMessage;
        
        // We don't process private or channel messages here.
        if (message?.Chat.Type is not (ChatType.Group or ChatType.Supergroup))
            return;

        await groupChatService.HandleGroupMessage(message, ct);

        context.SetExecutedRoute(new ExecutedRouteInfo(nameof(HandleGroupMessageMiddleware), message.Text));
    }
}