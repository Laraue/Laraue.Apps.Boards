using Laraue.Apps.Boards.TelegramServices;
using Laraue.Apps.Boards.TelegramServices.Services.Messages;
using Laraue.Telegram.NET.Abstractions.Request;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;

namespace Laraue.Apps.Boards.TelegramHost.Controllers;

public class ReplyModeController(IReplyModeService replyModeService) : TelegramController
{
    [TelegramCallbackRoute(TelegramRoutes.SwitchToReactions)]
    public Task HandleSwitchToReactions(
        RequestContext requestContext,
        CancellationToken cancellationToken)
    {
        return replyModeService.HandleSwitchToReactions(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            cancellationToken);
    }
}
