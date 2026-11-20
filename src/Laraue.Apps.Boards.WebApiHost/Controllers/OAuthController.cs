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
        [FromQuery(Name = "response_type")] string? responseType,
        [FromQuery(Name = "client_id")] string? clientId,
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        [FromQuery(Name = "code_challenge")] string? codeChallenge,
        [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod,
        [FromQuery(Name = "state")] string? state,
        [FromQuery(Name = "resource")] string? resource,
        CancellationToken cancellationToken = default)
    {
        var redirectUrl = await oauthService.Authorize(
            new OAuthAuthorizeRequest(
                responseType,
                clientId,
                redirectUri,
                codeChallenge,
                codeChallengeMethod,
                state,
                resource),
            cancellationToken);

        return Redirect(redirectUrl);
    }

    [AllowAnonymous]
    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token(
        [FromForm(Name = "grant_type")] string? grantType,
        [FromForm(Name = "code")] string? code,
        [FromForm(Name = "client_id")] string? clientId,
        [FromForm(Name = "redirect_uri")] string? redirectUri,
        [FromForm(Name = "code_verifier")] string? codeVerifier,
        [FromForm(Name = "resource")] string? resource,
        CancellationToken cancellationToken = default)
    {
        var response = await oauthService.Token(
            new OAuthTokenFormRequest(
                grantType,
                code,
                clientId,
                redirectUri,
                codeVerifier,
                resource),
            cancellationToken);

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
