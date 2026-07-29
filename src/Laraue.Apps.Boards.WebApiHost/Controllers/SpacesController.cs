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
    
    [HttpPut("{key}")]
    public Task Update(
        string key,
        [FromBody] UpdateSpaceRequest request,
        CancellationToken cancellationToken = default)
    {
        return spacesService.Update(
            request with
            {
                OldKey = key,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    [HttpDelete("{key}")]
    public Task Delete(
        string key,
        CancellationToken cancellationToken = default)
    {
        return spacesService.Delete(
            new DeleteSpaceRequest
            {
                Key = key,
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
    
    [HttpGet("{key}/epics")]
    public Task<EpicListDto[]> GetSpaceEpics(
        string key,
        CancellationToken cancellationToken = default) => 
        epicsService.GetSpaceEpics(
            new GetEpicsRequest
            {
                Key = key,
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