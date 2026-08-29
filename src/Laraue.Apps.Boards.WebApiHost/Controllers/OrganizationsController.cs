using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemas.User)]
[ApiController]
[Route("/api/organizations")]
public class OrganizationsController(
    IOrganizationsService organizationsService,
    IOrganizationHistoryService organizationHistoryService,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost]
    public Task<CreateOrganizationResponse> Create(
        [FromBody] CreateOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        return organizationsService.Create(
            request with
            {
                UserId = HttpContext.User.GetId()
            },
            cancellationToken);
    }

    [HttpGet]
    public Task<OrganizationListDto[]> GetOrganizations(
        CancellationToken cancellationToken = default)
    {
        return organizationsService.GetOrganizations(
            new GetOrganizationsRequest
            {
                UserId = HttpContext.User.GetId(),
            },
            cancellationToken);
    }

    [HttpPost("join/{code}")]
    public Task Join(
        string code,
        CancellationToken cancellationToken = default)
    {
        return organizationsService.Join(
            new JoinOrganizationRequest
            {
                JoinCode = code,
                UserId = HttpContext.User.GetId(),
            },
            cancellationToken);
    }

    [HttpPost("{id:long}/leave")]
    public Task Leave(
        long id,
        CancellationToken cancellationToken = default)
    {
        return organizationsService.Leave(
            new LeaveOrganizationRequest
            {
                UserId = HttpContext.User.GetId(),
                OrganizationId = id,
            },
            cancellationToken);
    }

    [HttpPost("login")]
    public async Task<string> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = await organizationsService.Login(
            request with
            {
                UserId = HttpContext.User.GetId(),
            },
            cancellationToken);
        AuthCookies.Append(Response, AuthCookies.Organization, token, environment);
        return token;
    }

    [Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
    [HttpGet("current")]
    public Task<OrganizationDto> GetOrganization(
        CancellationToken cancellationToken = default)
    {
        return organizationsService.GetOrganization(
            new GetOrganizationRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }

    [Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
    [HttpPost("history")]
    public Task<ShortPaginatedResult<OrganizationHistoryItem>> GetOrganizationHistory(
        [FromBody] GetOrganizationHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        return organizationHistoryService.GetOrganizationHistory(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }

    [Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
    [HttpGet("members")]
    public Task<VisibleUser[]> GetMembers(
        [Microsoft.AspNetCore.Mvc.FromQuery] string? spaceKey = null,
        CancellationToken cancellationToken = default)
    {
        return organizationsService.GetMembers(
            new GetMembersRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                SpaceKey = spaceKey,
            },
            cancellationToken);
    }

    [Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
    [HttpGet("attributes")]
    public Task<AttributeDto[]> GetAttributes(
        CancellationToken cancellationToken = default)
    {
        return organizationsService.GetAttributes(
            new GetAttributesRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }
}
