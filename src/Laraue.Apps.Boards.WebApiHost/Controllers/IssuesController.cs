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
    [Consumes("multipart/form-data")]
    public Task<string> Create(
        [FromForm] CreateIssueRequest request,
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
    [Consumes("multipart/form-data")]
    public Task Update(
        [FromRoute] string key,
        [FromForm] UpdateIssueRequest request,
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
    
    [HttpPost("comments")]
    [Consumes("multipart/form-data")]
    public Task<long> AddComment(
        [FromForm] AddCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        return issuesService.AddIssueComment(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    [HttpPut("comments/{id:long}")]
    [Consumes("multipart/form-data")]
    public Task UpdateComment(
        long id,
        [FromForm] UpdateCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        return issuesService.UpdateIssueComment(
            request with
            {
                CommentId = id,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    [HttpDelete("comments/{id:long}")]
    public Task DeleteComment(
        long id,
        CancellationToken cancellationToken = default)
    {
        return issuesService.DeleteIssueComment(
            new DeleteCommentRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                CommentId = id,
            },
            cancellationToken);
    }
    
    [HttpPost("order")]
    public Task UpdateOrder(
        [FromBody] ChangesIssuesOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        return issuesService.ChangesIssuesOrder(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    [HttpPost("status")]
    public Task<Dictionary<string, string>> UpdateStatus(
        [FromBody] UpdateIssuesStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        return issuesService.UpdateIssuesStatus(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    [HttpPost("{key}/comments")]
    public Task<ShortPaginatedResult<CommentDto>> GetIssueComments(
        string key,
        [FromBody] GetIssueCommentsRequest request,
        CancellationToken cancellationToken = default)
    {
        return issuesService.GetIssueComments(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                IssueKey = key,
            },
            cancellationToken);
    }
    
    [HttpPost("{key}/history")]
    public Task<ShortPaginatedResult<IssueHistoryItem>> GetIssueHistory(
        string key,
        [FromBody] GetIssueHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        return issuesService.GetIssueHistory(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                IssueKey = key,
            },
            cancellationToken);
    }
}
