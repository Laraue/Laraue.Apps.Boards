using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CreateIssueRequest = Laraue.Apps.Boards.WebApiServices.CreateIssueRequest;

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
    
    [HttpGet("{key}")]
    public Task<IssueDetailDto> GetIssue(
        string key,
        CancellationToken cancellationToken = default)
    {
        return issuesService.GetIssue(
            new GetIssueRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                IssueKey = new IssueKey(key),
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
    
    [HttpDelete("{key}")]
    public Task Delete(
        string key,
        CancellationToken cancellationToken = default)
    {
        return issuesService.Delete(
            new DeleteIssueRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                IssueKey = new IssueKey(key),
            },
            cancellationToken);
    }
    
    [HttpPost]
    public Task<string> Create(
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
    
    [HttpPut("{key}")]
    public Task Update(
        [FromRoute] string key,
        [FromBody] UpdateIssueRequest request,
        CancellationToken cancellationToken = default)
    {
        return issuesService.Update(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                IssueKey = new IssueKey(key),
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

    [HttpPost("{key}/add-attachment")]
    public async Task<MediaInfo> UploadFile(
        string key,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();

        return await issuesService.UploadAttachment(
            new UploadAttachmentRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                ContentType = file.ContentType,
                FileName = file.FileName,
                Stream = stream,
                IssueKey = new IssueKey(key),
            },
            cancellationToken);
    }
}
