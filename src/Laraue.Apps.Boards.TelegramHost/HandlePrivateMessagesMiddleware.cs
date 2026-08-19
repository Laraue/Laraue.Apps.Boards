using Laraue.Apps.Boards.TelegramServices;
using Laraue.Apps.Boards.TelegramServices.Services.Messages;
using Laraue.Telegram.NET.Abstractions;
using Laraue.Telegram.NET.Core.Extensions;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.TelegramHost;

public class HandlePrivateMessagesMiddleware(
    RequestContext context,
    ITelegramMessageService telegramMessageService)
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

        if (context.GetExecutedRoute() is null && AllowedUpdates.Contains(context.Update.Type))
        {
            var message = context.Update.Message ?? context.Update.EditedMessage;

            // We don't process group messages here.
            if (message!.Chat.Type != ChatType.Private)
                return;

            // This message was produced by the user picking an inline query result
            if (message.ViaBot is not null)
            {
                context.SetExecutedRoute(
                    new ExecutedRouteInfo(
                        nameof(HandlePrivateMessagesMiddleware),
                        "ViaBot message skipped"));
                return;
            }

            var text = message.Text;

            // A private chat's id is always equal to the user's own Telegram id - use the
            // user id here rather than Chat.Id for parity with how it was resolved before.
            var externalChatId = context.Update.GetUserId();
            var request = SaveMessageTelegramRequestFactory.Create(message, context.UserId, externalChatId);

            if (request is not null)
                await telegramMessageService.HandleSaveMessage(request, ct);

            context.SetExecutedRoute(
                new ExecutedRouteInfo("HandleAllMessagesMiddleware", text));
        }
    }
}
