using Laraue.Apps.Boards.TelegramServices;
using Laraue.Apps.Boards.TelegramServices.Services.Messages;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.TelegramHost.Controllers;

public class InfoController(IInfoCommandService infoCommandService) : TelegramController
{
    [TelegramMessageRoute(TelegramRoutes.InfoCommand, ChatType.Group, ChatType.Supergroup, ChatType.Private)]
    public Task HandleInfo(RequestContext requestContext, CancellationToken cancellationToken)
    {
        return infoCommandService.HandleInfoCommand(
            requestContext.Update.Message!,
            requestContext.UserId,
            cancellationToken);
    }
}
