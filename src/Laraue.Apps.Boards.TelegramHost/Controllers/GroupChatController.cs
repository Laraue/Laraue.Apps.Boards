using Laraue.Apps.Boards.TelegramServices;
using Laraue.Apps.Boards.TelegramServices.Services.GroupChats;
using Laraue.Telegram.NET.Abstractions.Request;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.TelegramHost.Controllers;

public class GroupChatController(IGroupChatLinkFlowService linkFlowService)
    : TelegramController
{
    [TelegramMessageRoute("/link")]
    public Task HandleLink(RequestContext requestContext, CancellationToken cancellationToken)
    {
        var message = requestContext.Update.Message;
        if (message is null || message.Chat.Type is not (ChatType.Group or ChatType.Supergroup))
            return Task.CompletedTask;

        return linkFlowService.HandleLinkCommand(message, requestContext.UserId, cancellationToken);
    }

    [TelegramCallbackRoute("link/org/{orgId}")]
    public Task HandleOrgSelected(
        RequestContext requestContext,
        [FromPath] long orgId,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleOrgSelected(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            orgId,
            cancellationToken);
    }

    [TelegramCallbackRoute("link/space/{spaceId}")]
    public Task HandleSpaceSelected(
        RequestContext requestContext,
        [FromPath] long spaceId,
        [FromQuery] long orgId,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleSpaceSelected(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            orgId,
            spaceId,
            cancellationToken);
    }

    [TelegramCallbackRoute("link/backlog/{spaceId}")]
    public Task HandleUseBacklog(
        RequestContext requestContext,
        [FromPath] long spaceId,
        [FromQuery] long orgId,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleUseBacklog(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            orgId,
            spaceId,
            cancellationToken);
    }

    [TelegramCallbackRoute("link/epics/{spaceId}")]
    public Task HandleChooseEpicAndStatus(
        RequestContext requestContext,
        [FromPath] long spaceId,
        [FromQuery] long orgId,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleChooseEpicAndStatus(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            orgId,
            spaceId,
            cancellationToken);
    }

    [TelegramCallbackRoute("link/epic/{epicId}")]
    public Task HandleEpicSelected(
        RequestContext requestContext,
        [FromPath] long epicId,
        [FromQuery] long spaceId,
        [FromQuery] long orgId,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleEpicSelected(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            orgId,
            spaceId,
            epicId,
            cancellationToken);
    }

    [TelegramCallbackRoute("link/status/{statusId}")]
    public Task HandleStatusSelected(
        RequestContext requestContext,
        [FromPath] long statusId,
        [FromQuery] long epicId,
        [FromQuery] long spaceId,
        [FromQuery] long orgId,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleStatusSelected(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            orgId,
            spaceId,
            epicId,
            statusId,
            cancellationToken);
    }

    [TelegramCallbackRoute("link/back")]
    public Task HandleBack(RequestContext requestContext, CancellationToken cancellationToken)
    {
        return linkFlowService.HandleBack(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            cancellationToken);
    }

    [TelegramCallbackRoute("link/change")]
    public Task HandleChangeLink(RequestContext requestContext, CancellationToken cancellationToken)
    {
        return linkFlowService.HandleChangeLink(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            cancellationToken);
    }

    [TelegramCallbackRoute("link/unlink")]
    public Task HandleUnlinkCallback(RequestContext requestContext, CancellationToken cancellationToken)
    {
        return linkFlowService.HandleUnlinkCallback(requestContext.Update.CallbackQuery!, cancellationToken);
    }

    [TelegramMessageRoute("/unlink")]
    public Task HandleUnlink(RequestContext requestContext, CancellationToken cancellationToken)
    {
        var message = requestContext.Update.Message;
        if (message is null || message.Chat.Type is not (ChatType.Group or ChatType.Supergroup))
            return Task.CompletedTask;

        return linkFlowService.HandleUnlinkCommand(message, cancellationToken);
    }
}
