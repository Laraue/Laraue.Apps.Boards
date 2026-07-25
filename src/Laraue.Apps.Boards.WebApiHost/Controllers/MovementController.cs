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
    
    [HttpPost("issue/{key}/move-to-status/{statusId:long}")]
    public Task MoveIssue(
        string key,
        long statusId,
        CancellationToken cancellationToken = default)
    {
        return service.MoveIssue(
            new MoveIssueRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                IssueKey = new IssueKey(key),
                StatusId = statusId
            },
            cancellationToken);
    }
    
    [HttpPost("issue/{issueKey}/set-after/{previousIssueKey}")]
    public Task SetIssueOrder(
        string issueKey,
        string previousIssueKey,
        [FromBody] SetIssueOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        return service.SetIssueOrder(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                IssueKey = new IssueKey(issueKey),
                PreviousIssueKey = new IssueKey(previousIssueKey),
            },
            cancellationToken);
    }
}