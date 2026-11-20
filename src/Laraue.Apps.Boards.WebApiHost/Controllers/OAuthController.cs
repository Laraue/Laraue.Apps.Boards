using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.WebApiServices.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

/// <summary>
/// The OAuth authorization server for MCP clients (BRD-362). The authorize and token requests are anonymous and speak
/// OAuth - parameters in snake case, errors as OAuth error codes; the consent page's calls use the signed-in user's
/// token, since the organization is chosen on that page.
/// </summary>
[Authorize(AuthenticationSchemes = AuthSchemas.User)]
[ApiController]
[Route("/api/oauth")]
public class OAuthController(IOAuthService oauthService) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("authorize")]
    public async Task<IActionResult> Authorize(
        [FromQuery] OAuthAuthorizeRequest request,
        CancellationToken cancellationToken = default)
    {
        return Redirect(await oauthService.Authorize(request, cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token(
        [FromForm] OAuthTokenFormRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await oauthService.Token(request, cancellationToken);

        // RFC 6749 5.1: a token response is never cached.
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        return response.IsError ? BadRequest(response) : Ok(response);
    }

    [HttpGet("requests/{requestId:guid}")]
    public Task<OAuthConsentDto> GetConsent(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        return oauthService.GetConsent(requestId, cancellationToken);
    }

    [HttpPost("requests/{requestId:guid}/approve")]
    public Task<OAuthDecisionResponse> Approve(
        Guid requestId,
        [FromBody] ApproveOAuthRequest request,
        CancellationToken cancellationToken = default)
    {
        return oauthService.Approve(
            requestId,
            HttpContext.User.GetId(),
            request,
            cancellationToken);
    }

    [HttpPost("requests/{requestId:guid}/deny")]
    public Task<OAuthDecisionResponse> Deny(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        return oauthService.Deny(
            requestId,
            HttpContext.User.GetId(),
            cancellationToken);
    }
}
