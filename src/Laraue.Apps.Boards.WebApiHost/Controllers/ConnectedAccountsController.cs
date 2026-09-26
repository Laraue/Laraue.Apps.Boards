using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemas.User)]
[ApiController]
[Route("/api/user/connected-accounts")]
public class ConnectedAccountsController(
    ITelegramAuthService telegramAuthService,
    IConnectedAccountsService connectedAccountsService)
    : ControllerBase
{
    [HttpPost("telegram")]
    public Task<ConnectAccountResponse> ConnectTelegram(
        [FromBody] TelegramWidgetAuthRequest request,
        CancellationToken cancellationToken)
    {
        return telegramAuthService.ConnectTelegram(HttpContext.User.GetId(), request, cancellationToken);
    }

    [HttpPost("google")]
    public Task<ConnectAccountResponse> ConnectGoogle(
        [FromBody] ConnectGoogleAccountRequest request,
        CancellationToken cancellationToken)
    {
        return connectedAccountsService.ConnectGoogle(HttpContext.User.GetId(), request, cancellationToken);
    }
}
