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
    
    [HttpPost("space/{spaceKey}/epics-to-space/{newSpaceKey}")]
    public Task MoveSpaceEpics(
        string spaceKey,
        string newSpaceKey,
        CancellationToken cancellationToken = default)
    {
        return service.MoveSpaceEpics(
            new MoveSpaceEpicsRequest
            {
                SpaceKey = spaceKey,
                NewSpaceKey = newSpaceKey,
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }
    
    [HttpPost("epic/{id:long}/to-space/{newSpaceKey}")]
    public Task MoveEpic(
        long id,
        string newSpaceKey,
        CancellationToken cancellationToken = default)
    {
        return service.MoveEpic(
            new MoveEpicRequest
            {
                Id = id,
                NewSpaceKey = newSpaceKey,
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