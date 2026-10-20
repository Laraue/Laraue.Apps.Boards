using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

/// <summary>
/// "Connect this machine" for the agent runner (BRD-309). Starting and polling are anonymous - the runner has no
/// credentials yet, and the device code is its secret; the Allow page's calls use the signed-in user's token, since
/// the organization is chosen on that page.
/// </summary>
[Authorize(AuthenticationSchemes = AuthSchemas.User)]
[ApiController]
[Route("/api/agent-sign-in")]
public class AgentSignInController(IAgentSignInService agentSignInService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost]
    public Task<StartAgentSignInResponse> Start(
        [FromBody] StartAgentSignInRequest request,
        CancellationToken cancellationToken = default)
    {
        return agentSignInService.Start(request, cancellationToken);
    }

    [AllowAnonymous]
    [HttpPost("token")]
    public Task<PollAgentSignInResponse> Poll(
        [FromBody] PollAgentSignInRequest request,
        CancellationToken cancellationToken = default)
    {
        return agentSignInService.Poll(request, cancellationToken);
    }

    [HttpGet("{userCode}")]
    public Task<AgentSignInDto> Get(
        string userCode,
        CancellationToken cancellationToken = default)
    {
        return agentSignInService.Get(userCode, cancellationToken);
    }

    [HttpPost("{userCode}/approve")]
    public Task Approve(
        string userCode,
        [FromBody] ApproveAgentSignInRequest request,
        CancellationToken cancellationToken = default)
    {
        return agentSignInService.Approve(
            userCode,
            HttpContext.User.GetId(),
            request,
            cancellationToken);
    }

    [HttpPost("{userCode}/deny")]
    public Task Deny(
        string userCode,
        CancellationToken cancellationToken = default)
    {
        return agentSignInService.Deny(
            userCode,
            HttpContext.User.GetId(),
            cancellationToken);
    }
}
