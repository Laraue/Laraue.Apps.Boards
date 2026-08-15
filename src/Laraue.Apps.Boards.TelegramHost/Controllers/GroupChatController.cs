using Laraue.Apps.Boards.TelegramServices;
using Laraue.Apps.Boards.TelegramServices.Services.GroupChats;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.TelegramHost.Controllers;

public class GroupChatController(IGroupChatLinkService linkFlowService) : TelegramController
{
    [TelegramMessageRoute(TelegramRoutes.LinkCommand, ChatType.Group, ChatType.Supergroup)]
    public Task HandleLink(RequestContext requestContext, CancellationToken cancellationToken)
    {
        return linkFlowService.HandleLinkCommand(
            requestContext.UserId,
            requestContext.Update.Message!,
            cancellationToken);
    }
}