using Laraue.Apps.Boards.TelegramServices;
using Laraue.Apps.Boards.TelegramServices.Services.Messages;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.TelegramHost.Controllers;

public class SaveController(ISaveCommandService saveCommandService) : TelegramController
{
    [TelegramMessageRoute(TelegramRoutes.SaveCommand, ChatType.Group, ChatType.Supergroup, ChatType.Private)]
    public Task HandleSave(RequestContext requestContext, CancellationToken cancellationToken)
    {
        return saveCommandService.HandleSaveCommand(
            requestContext.Update.Message!,
            requestContext.UserId,
            cancellationToken);
    }

    [TelegramMessageRoute(TelegramRoutes.AiSaveCommand, ChatType.Group, ChatType.Supergroup, ChatType.Private)]
    public Task HandleAiSave(RequestContext requestContext, CancellationToken cancellationToken)
    {
        return saveCommandService.HandleAiSaveCommand(
            requestContext.Update.Message!,
            requestContext.UserId,
            cancellationToken);
    }
}
