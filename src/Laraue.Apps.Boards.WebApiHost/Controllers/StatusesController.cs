using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DeleteStatusRequest = Laraue.Apps.Boards.WebApiServices.DeleteStatusRequest;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
[ApiController]
[Route("/api/statuses")]
public class StatusesController(IStatusesService statusesService)
    : ControllerBase
{
    [HttpPost]
    public Task<long> CreateStatus(
        [FromBody] CreateStatusRequest request,
        CancellationToken cancellationToken)
    {
        return statusesService.CreateStatus(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }
    
    [HttpDelete("{id:long}")]
    public Task Delete(
        long id,
        CancellationToken cancellationToken)
    {
        return statusesService.Delete(
            new DeleteStatusRequest
            {
                Id = id,
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }

    [HttpPut("{id:long}")]
    public Task Edit(
        long id,
        [FromBody] EditStatusRequest request,
        CancellationToken cancellationToken)
    {
        return statusesService.Edit(
            request with
            {
                Id = id,
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }

    [HttpGet]
    public Task<MessageStatusDto[]> GetStatuses(
        [FromQuery] GetStatusesRequest request,
        CancellationToken cancellationToken)
    {
        return statusesService.GetStatuses(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
}   