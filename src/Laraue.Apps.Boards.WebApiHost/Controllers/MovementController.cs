using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
[ApiController]
[Route("/api/movement")]
public class MovementController(IMovementService service) : ControllerBase
{
    [HttpPost("space/{id:long}/to-organization/{organizationId:long}")]
    public Task MoveSpace(
        long id,
        long organizationId,
        CancellationToken cancellationToken = default)
    {
        return service.MoveSpace(
            new MoveSpaceRequest
            {
                Id = id,
                NewOrganizationId = organizationId,
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }
    
    [HttpPost("space/{id:long}/epics-to-space/{newSpaceId:long}")]
    public Task MoveSpaceEpics(
        long id,
        long newSpaceId,
        CancellationToken cancellationToken = default)
    {
        return service.MoveSpaceEpics(
            new MoveSpaceEpicsRequest
            {
                SpaceId = id,
                NewSpaceId = newSpaceId,
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }
    
    [HttpPost("epic/{id:long}/to-space/{newSpaceId:long}")]
    public Task MoveEpic(
        long id,
        long newSpaceId,
        CancellationToken cancellationToken = default)
    {
        return service.MoveEpic(
            new MoveEpicRequest
            {
                Id = id,
                NewSpaceId = newSpaceId,
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