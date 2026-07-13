using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[ApiController]
[Route("/api/user")]
public class TelegramAuthController(
    ITelegramAuthService authService,
    IWebHostEnvironment environment)
    : ControllerBase
{
    [HttpPost("auth-via-mini-app")]
    public async Task<string> Authenticate(
        AuthenticateViaStringInitDataRequest request,
        CancellationToken cancellationToken)
    {
        var token = await authService.Authenticate(request, cancellationToken);
        AuthCookies.Append(Response, AuthCookies.User, token, environment);
        return token;
    }
    
    [HttpPost("auth")]
    public async Task<string> Authenticate(
        TelegramWidgetAuthRequest request,
        CancellationToken cancellationToken)
    {
        var token = await authService.Authenticate(request, cancellationToken);
        AuthCookies.Append(Response, AuthCookies.User, token, environment);
        return token;
    }

    [HttpPost("logout")]
    public Task Logout()
    {
        AuthCookies.Delete(Response, environment);
        return Task.CompletedTask;
    }
}
