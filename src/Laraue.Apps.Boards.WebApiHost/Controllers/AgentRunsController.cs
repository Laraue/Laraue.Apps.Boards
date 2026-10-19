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
