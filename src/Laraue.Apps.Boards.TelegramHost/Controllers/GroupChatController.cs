using Laraue.Apps.Boards.TelegramServices;
using Laraue.Apps.Boards.TelegramServices.Services.GroupChats;
using Laraue.Telegram.NET.Abstractions.Request;
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
            requestContext.Update.Message!,
            requestContext.UserId,
            cancellationToken);
    }

    [TelegramCallbackRoute(TelegramRoutes.LinkOrganization)]
    public Task HandleOrganizationSelected(
        RequestContext requestContext,
        [FromPath] long id,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleOrganizationSelected(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            id,
            cancellationToken);
    }
}