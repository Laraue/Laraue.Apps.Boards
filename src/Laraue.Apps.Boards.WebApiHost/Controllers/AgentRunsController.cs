using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
[ApiController]
[Route("/api/agent-runs")]
public class AgentRunsController(IAgentRunsService agentRunsService) : ControllerBase
{
    /// <summary>
    /// Hands the issue to the agent: creates a Ready run.
    /// </summary>
    [HttpPost]
    public Task<AgentRunDto> Create(
        [FromBody] CreateAgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        return agentRunsService.Create(
            request,
            HttpContext.User.GetOrganizationAuthData(),
            cancellationToken);
    }

    /// <summary>
    /// Sends an issue the agent worked on back to it: the comment, the move to a not started status and the new run,
    /// in one step.
    /// </summary>
    [HttpPost("send-back")]
    public Task<AgentRunDto> SendBack(
        [FromBody] SendBackAgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        return agentRunsService.SendBack(
            request,
            HttpContext.User.GetOrganizationAuthData(),
            cancellationToken);
    }

    [HttpPost("{id:long}/cancel")]
    public Task<bool> Cancel(
        long id,
        CancellationToken cancellationToken = default)
    {
        return agentRunsService.Cancel(
            id,
            HttpContext.User.GetOrganizationAuthData(),
            cancellationToken);
    }
}
