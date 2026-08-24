using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
[ApiController]
[Route("/api/epics")]
public class EpicsController(IEpicsService categoriesService)
    : ControllerBase
{
    [HttpGet("{id}")]
    public Task<EpicDto> Get(
        [FromRoute] long id,
        CancellationToken cancellationToken = default) => 
        categoriesService.GetEpic(
            new GetEpicRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                Id = id
            },
            cancellationToken);

    [HttpPost]
    public Task<long> Create(
        [FromBody] CreateEpicRequest request,
        CancellationToken cancellationToken = default)
    {
        return categoriesService.Create(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }
    
    [HttpPost("get-with-statuses")]
    public Task<ShortPaginatedResult<EpicStatusesDto>> GetWithStatuses(
        [FromBody] SearchEpicStatusesRequest request,
        CancellationToken cancellationToken = default)
    {
        return categoriesService.SearchEpicsWithStatuses(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }

    [HttpPost("{id:long}/status")]
    public Task ChangeStatus(
        [FromRoute] long id,
        [FromBody] ChangeEpicStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        return categoriesService.ChangeStatus(
            request with
            {
                Id = id,
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }

    [HttpPost("{id:long}/reorder-statuses")]
    public Task ChangeStatusesOrder(
        [FromRoute] long id,
        [FromBody] IReadOnlyDictionary<long, int> order,
        CancellationToken cancellationToken = default)
    {
        return categoriesService.ChangeStatusesOrder(
            new ChangeStatusesOrderRequest
            {
                EpicId = id,
                Order = order,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    [HttpPut("{id:long}")]
    public Task Update(
        long id,
        [FromBody] UpdateEpicRequest request,
        CancellationToken cancellationToken = default)
    {
        return categoriesService.Update(
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
        return categoriesService.Delete(
            new DeleteEpicRequest
            {
                Id = id,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
}   