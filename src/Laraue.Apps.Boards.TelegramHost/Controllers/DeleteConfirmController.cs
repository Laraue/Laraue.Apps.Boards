using Laraue.Apps.Boards.TelegramServices;
using Laraue.Apps.Boards.TelegramServices.Services.Messages;
using Laraue.Telegram.NET.Abstractions.Request;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;

namespace Laraue.Apps.Boards.TelegramHost.Controllers;

public class DeleteConfirmController(IDeleteCommandService deleteCommandService) : TelegramController
{
    [TelegramCallbackRoute(TelegramRoutes.DeleteConfirm)]
    public Task HandleDeleteConfirmed(
        RequestContext requestContext,
        [FromPath] long issueId,
        CancellationToken cancellationToken)
    {
        return deleteCommandService.HandleDeleteConfirmed(
            requestContext.Update.CallbackQuery!,
            requestContext.UserId,
            issueId,
            cancellationToken);
    }
}
