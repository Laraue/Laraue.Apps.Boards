using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.TelegramServices;
using Laraue.Apps.Boards.TelegramServices.Services.GroupChats;
using Laraue.Telegram.NET.Abstractions.Request;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.TelegramHost.Controllers;

public class LinkGroupController(IGroupChatLinkService linkFlowService) : TelegramController
{
    [TelegramMessageRoute(TelegramRoutes.LinkCommand, ChatType.Group, ChatType.Supergroup, ChatType.Private)]
    public Task HandleLink(RequestContext requestContext, CancellationToken cancellationToken)
    {
        return linkFlowService.HandleLinkCommand(
            requestContext.Update.Message!,
            requestContext.UserId,
            cancellationToken);
    }

    [TelegramMessageRoute(TelegramRoutes.UnlinkCommand, ChatType.Group, ChatType.Supergroup, ChatType.Private)]
    public Task HandleUnlink(RequestContext requestContext, CancellationToken cancellationToken)
    {
        return linkFlowService.HandleUnlinkCommand(
            requestContext.Update.Message!,
            requestContext.UserId,
            cancellationToken);
    }
    
    [TelegramCallbackRoute(TelegramRoutes.BackToLink)]
    public Task HandleBackToLink(RequestContext requestContext, CancellationToken cancellationToken)
    {
        return linkFlowService.HandleBackToOrganizations(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            cancellationToken);
    }

    [TelegramCallbackRoute(TelegramRoutes.LinkOrganization)]
    public Task HandleOrganizationSelected(
        RequestContext requestContext,
        [FromPath(TelegramRoutes.Parameters.Id)] long id,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleOrganizationSelected(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            id,
            cancellationToken);
    }
    
    [TelegramCallbackRoute(TelegramRoutes.LinkSpace)]
    public Task HandleSpaceSelected(
        RequestContext requestContext,
        [FromPath(TelegramRoutes.Parameters.Id)] long id,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleSpaceSelected(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            id,
            cancellationToken);
    }
    
    [TelegramCallbackRoute(TelegramRoutes.LinkEpic)]
    public Task HandleEpicSelected(
        RequestContext requestContext,
        [FromPath(TelegramRoutes.Parameters.Id)] long id,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleEpicSelected(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            id,
            cancellationToken);
    }
    
    [TelegramCallbackRoute(TelegramRoutes.LinkStatus)]
    public Task HandleStatusSelected(
        RequestContext requestContext,
        [FromPath(TelegramRoutes.Parameters.Id)] long id,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleStatusSelected(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            id,
            cancellationToken);
    }

    [TelegramCallbackRoute(TelegramRoutes.LinkChooseEachTime)]
    public Task HandleChooseEachTime(
        RequestContext requestContext,
        [FromPath(TelegramRoutes.Parameters.DestinationType)] LinkDestinationType destinationType,
        [FromPath(TelegramRoutes.Parameters.DestinationId)] long destinationId,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleChooseEachTime(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            destinationType,
            destinationId,
            cancellationToken);
    }

    [TelegramCallbackRoute(TelegramRoutes.LinkSaveMode)]
    public Task HandleSaveModeSelected(
        RequestContext requestContext,
        [FromPath(TelegramRoutes.Parameters.DestinationType)] LinkDestinationType destinationType,
        [FromPath(TelegramRoutes.Parameters.DestinationId)] long destinationId,
        [FromPath(TelegramRoutes.Parameters.Mode)] SaveMode mode,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleSaveModeSelected(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            destinationType,
            destinationId,
            mode,
            cancellationToken);
    }

    [TelegramCallbackRoute(TelegramRoutes.LinkReplyMode)]
    public Task HandleReplyModeSelected(
        RequestContext requestContext,
        [FromPath(TelegramRoutes.Parameters.DestinationType)] LinkDestinationType destinationType,
        [FromPath(TelegramRoutes.Parameters.DestinationId)] long destinationId,
        [FromPath(TelegramRoutes.Parameters.SaveMode)] SaveMode saveMode,
        [FromPath(TelegramRoutes.Parameters.ReplyMode)] ReplyMode replyMode,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleReplyModeSelected(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            destinationType,
            destinationId,
            saveMode,
            replyMode,
            cancellationToken);
    }

    [TelegramCallbackRoute(TelegramRoutes.LinkChangeDestination)]
    public Task HandleChangeDestination(RequestContext requestContext, CancellationToken cancellationToken)
    {
        return linkFlowService.HandleChangeDestination(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            cancellationToken);
    }

    [TelegramCallbackRoute(TelegramRoutes.LinkSettingsSaveMode)]
    public Task HandleSettingsSaveModeSelected(
        RequestContext requestContext,
        [FromPath(TelegramRoutes.Parameters.Mode)] SaveMode mode,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleSettingsSaveModeSelected(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            mode,
            cancellationToken);
    }

    [TelegramCallbackRoute(TelegramRoutes.LinkSettingsReplyMode)]
    public Task HandleSettingsReplyModeSelected(
        RequestContext requestContext,
        [FromPath(TelegramRoutes.Parameters.Mode)] ReplyMode mode,
        CancellationToken cancellationToken)
    {
        return linkFlowService.HandleSettingsReplyModeSelected(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            mode,
            cancellationToken);
    }

    [TelegramCallbackRoute(TelegramRoutes.Unlink)]
    public Task HandleUnlinkCallback(RequestContext requestContext, CancellationToken cancellationToken)
    {
        return linkFlowService.HandleUnlink(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            cancellationToken);
    }
}