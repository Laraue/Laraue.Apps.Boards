using Laraue.Apps.Boards.TelegramServices;
using Laraue.Apps.Boards.TelegramServices.Services.Messages;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.TelegramHost.Controllers;

public class DeleteController(IDeleteCommandService deleteCommandService) : TelegramController
{
    [TelegramMessageRoute(TelegramRoutes.DeleteCommand, ChatType.Group, ChatType.Supergroup, ChatType.Private)]
    public Task HandleDelete(RequestContext requestContext, CancellationToken cancellationToken)
    {
        return deleteCommandService.HandleDeleteCommand(
            requestContext.Update.Message!,
            requestContext.UserId,
            cancellationToken);
    }
}
