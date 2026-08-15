using Laraue.Apps.Boards.TelegramServices;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.TelegramHost.Controllers;

public class CommandsController(ITelegramCommandsService commandsService)
    : TelegramController
{
    [TelegramMessageRoute("/start", ChatType.Private)]
    public Task HandleStart(
        RequestContext requestContext,
        CancellationToken cancellationToken)
    {
        return commandsService.HandleStart(
            ReplyData.FromMessageRequest(requestContext),
            cancellationToken);
    }
    
    [TelegramMessageRoute("/help", ChatType.Private)]
    public Task HandleHelp(
        RequestContext requestContext,
        CancellationToken cancellationToken)
    {
        return commandsService.HandleHelp(
            ReplyData.FromMessageRequest(requestContext),
            cancellationToken);
    }
}