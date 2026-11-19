using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.TelegramServices;
using Laraue.Apps.Boards.TelegramServices.Services.Messages;
using Laraue.Telegram.NET.Abstractions.Request;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;

namespace Laraue.Apps.Boards.TelegramHost.Controllers;

public class SaveDestinationController(ISaveDestinationPicker destinationPicker) : TelegramController
{
    [TelegramCallbackRoute(TelegramRoutes.SaveDestinationPicked)]
    public Task HandlePicked(
        RequestContext requestContext,
        [FromPath(TelegramRoutes.Parameters.MessageId)] long telegramMessageId,
        [FromPath(TelegramRoutes.Parameters.Summarize)] bool summarize,
        [FromPath(TelegramRoutes.Parameters.DestinationType)] LinkDestinationType destinationType,
        [FromPath(TelegramRoutes.Parameters.DestinationId)] long destinationId,
        CancellationToken cancellationToken)
    {
        return destinationPicker.HandlePicked(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            telegramMessageId,
            summarize,
            destinationType,
            destinationId,
            cancellationToken);
    }
}
