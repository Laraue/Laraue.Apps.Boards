using Laraue.Apps.Boards.TelegramServices;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;
using Telegram.Bot;

namespace Laraue.Apps.Boards.TelegramHost.Controllers;

public class CommonQueriesController(ITelegramBotClient botClient) : TelegramController
{
    [TelegramCallbackRoute(TelegramRoutes.CloseCallbackWindow)]
    public Task HandleCloseCallbackWindow(
        RequestContext requestContext,
        CancellationToken cancellationToken)
    {
        var message = requestContext.Update.CallbackQuery!.Message!;
        
        return botClient.DeleteMessage(message.Chat.Id, message.Id, cancellationToken);
    }
}