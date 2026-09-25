using Laraue.Apps.Boards.WebApiServices;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[ApiController]
[Route("/api/user")]
public class GoogleAuthController(
    IGoogleAuthService authService,
    IWebHostEnvironment environment)
    : ControllerBase
{
    [HttpPost("auth-via-google")]
    public async Task<string> Authenticate(
        [FromBody] GoogleAuthRequest request,
        CancellationToken cancellationToken)
    {
        var token = await authService.Authenticate(request, cancellationToken);
        AuthCookies.Append(Response, AuthCookies.User, token, environment);
        return token;
    }
}
