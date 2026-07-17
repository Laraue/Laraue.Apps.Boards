using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
[ApiController]
[Route("/api/issues")]
public class IssuesController(IIssuesService issuesService) : ControllerBase
{
    [HttpGet("by-status/{statusId:long}")]
    public Task<BatchResult<IssueListDto>> GetIssuesByStatus(
        long statusId,
        [FromQuery] GetIssuesRequest request,
        CancellationToken cancellationToken = default)
    {
        return issuesService.GetIssues(
            request with
            {
                StatusId = statusId,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }

    [HttpPost("by-status/{statusId:long}/search")]
    public Task<BatchResult<IssueListDto>> SearchIssuesByStatus(
        long statusId,
        [FromBody] GetIssuesRequest request,
        CancellationToken cancellationToken = default)
    {
        return issuesService.GetIssues(
            request with
            {
                StatusId = statusId,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    
    [HttpGet("{id:long}")]
    public Task<IssueDetailDto> GetIssue(
        long id,
        CancellationToken cancellationToken = default)
    {
        return issuesService.GetIssue(
            new GetIssueRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                IssueId = id,
            },
            cancellationToken);
    }
    
    [HttpPost("board")]
    public Task<ColumnIssues[]> GetBoard(
        [FromBody] GetBoardRequest request,
        CancellationToken cancellationToken = default)
    {
        return issuesService.GetBoard(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    [HttpDelete("{id:long}")]
    public Task Delete(
        long id,
        CancellationToken cancellationToken = default)
    {
        return issuesService.Delete(
            new DeleteIssueRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                IssueId = id,
            },
            cancellationToken);
    }
    
    [HttpPost]
    public Task<long> Create(
        [FromBody] CreateIssueRequest request,
        CancellationToken cancellationToken = default)
    {
        return issuesService.Create(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    [HttpPut("{id:long}")]
    public Task Update(
        [FromRoute] long id,
        [FromBody] UpdateIssueRequest request,
        CancellationToken cancellationToken = default)
    {
        return issuesService.Update(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                Id = id,
            },
            cancellationToken);
    }
    
    [HttpPost("search")]
    public Task<ShortPaginatedResult<SearchIssueDto>> Search(
        [FromBody] SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        return issuesService.Search(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }

    [HttpGet("summary")]
    public Task<EpicSummary[]> GetBoardSummary(
        [FromQuery] GetBoardSummaryRequest request,
        CancellationToken cancellationToken = default)
    {
        return issuesService.GetBoardSummary(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
}
