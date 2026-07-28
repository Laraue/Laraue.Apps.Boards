using Laraue.Apps.Boards.WebApiServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
[ApiController]
[Route("/api/spaces")]
public class SpacesController(ISpacesService spacesService, IEpicsService epicsService) : ControllerBase
{
    [HttpPost]
    public Task<string> Create(
        [FromBody] CreateSpaceRequest request,
        CancellationToken cancellationToken = default)
    {
        return spacesService.Create(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }
    
    [HttpPut("{id:long}")]
    public Task Update(
        long id,
        [FromBody] UpdateSpaceRequest request,
        CancellationToken cancellationToken = default)
    {
        return spacesService.Update(
            request with
            {
                Id = id,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    [HttpDelete("{id:long}")]
    public Task Delete(
        long id,
        CancellationToken cancellationToken = default)
    {
        return spacesService.Delete(
            new DeleteSpaceRequest
            {
                Id = id,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    [HttpGet]
    public Task<SpaceListDto[]> GetAll(
        CancellationToken cancellationToken = default)
    {
        return spacesService.GetSpaces(
            new GetSpacesRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    [HttpGet("{key}")]
    public Task<SpaceDetailsDto> Get(
        string key,
        CancellationToken cancellationToken = default)
    {
        return spacesService.GetSpace(
            new GetSpaceRequest
            {
                Key = key,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    [HttpGet("{id:long}/epics")]
    public Task<EpicListDto[]> GetSpaceEpics(
        long id,
        CancellationToken cancellationToken = default) => 
        epicsService.GetSpaceEpics(
            new GetEpicsRequest
            {
                SpaceId = id,
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    
    [Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
    [HttpGet("{key}/members")]
    public Task<SpaceMember[]> GetSpaceMembers(
        string key,
        CancellationToken cancellationToken = default)
    {
        return spacesService.GetMembers(
            new GetSpaceMembersRequest
            {
                Key = key,
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }
}