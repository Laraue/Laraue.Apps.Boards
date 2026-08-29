using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
[ApiController]
[Route("/api/movement")]
public class MovementController(IMovementService service) : ControllerBase
{
    [HttpPost("space/{key}/to-organization/{organizationId:long}")]
    public Task MoveSpace(
        string key,
        long organizationId,
        CancellationToken cancellationToken = default)
    {
        return service.MoveSpace(
            new MoveSpaceRequest
            {
                Key = key,
                NewOrganizationId = organizationId,
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }
    
    [HttpPost("move-space-epics")]
    public Task MoveSpaceEpics(
        [FromBody] MoveSpaceEpicsRequest request,
        CancellationToken cancellationToken = default)
    {
        return service.MoveSpaceEpics(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }
    
    [HttpPost("move-epic")]
    public Task MoveEpic(
        [FromBody] MoveEpicRequest request,
        CancellationToken cancellationToken = default)
    {
        return service.MoveEpic(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }
    
    [HttpGet("organization/{id:long}/spaces")]
    public Task<DestinationSpace[]> GetDestinationSpaces(
        long id,
        CancellationToken cancellationToken = default)
    {
        return service.GetDestinationSpaces(
            new GetDestinationSpacesRequest
            {
                OrganizationId = id,
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }
}