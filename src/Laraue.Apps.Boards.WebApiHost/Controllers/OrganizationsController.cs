using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Telegram.NET.Abstractions.Request;
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
    
    [HttpPut("{id:long}")]
    public Task Update(
        long id,
        [FromBody] EditOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        return organizationsService.Update(
            request with
            {
                Id = id,
                UserId = HttpContext.User.GetId(),
            },
            cancellationToken);
    }
    
    [HttpDelete("{id:long}")]
    public Task Delete(
        long id,
        CancellationToken cancellationToken = default)
    {
        return organizationsService.Delete(
            new DeleteOrganizationRequest
            {
                Id = id,
                UserId = HttpContext.User.GetId(),
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
    
    [Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
    [HttpPost("regenerate-join-code")]
    public Task<string> RegenerateCode(
        CancellationToken cancellationToken = default)
    {
        return organizationsService.RegenerateJoinCode(
            new RegenerateJoinCodeRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    [Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
    [HttpGet("join-code")]
    public Task<string?> GetJoinCode(
        CancellationToken cancellationToken = default)
    {
        return organizationsService.GetOrganizationJoinCode(
            new GetOrganizationJoinCodeRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    [Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
    [HttpPost("revoke-access/{organizationUserId:long}")]
    public Task RevokeAccess(
        long organizationUserId,
        CancellationToken cancellationToken = default)
    {
        return organizationsService.RevokeAccess(
            new RevokeAccessRequest
            {
                OrganizationUserId = organizationUserId,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
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
    [HttpGet("permissions/{organizationUserId:long}")]
    public Task<UserPermissions> GetUserPermissions(
        long organizationUserId,
        CancellationToken cancellationToken = default)
    {
        return organizationsService.GetUserPermissions(
            new GetUserPermissionsRequest
            {
                OrganizationUserId = organizationUserId,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
    
    [Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
    [HttpPost("permissions/{organizationUserId:long}")]
    public Task SetUserPermissions(
        long organizationUserId,
        [FromBody] SetPermissionsRequest request,
        CancellationToken cancellationToken = default)
    {
        return organizationsService.SetUserPermissions(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                OrganizationUserId = organizationUserId
            },
            cancellationToken);
    }
    
    [Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
    [HttpGet("permittable-entities")]
    public Task<PermittableSpace[]> GetPermittableEntities(
        CancellationToken cancellationToken = default)
    {
        return organizationsService.GetPermittableEntities(
            new GetPermittableEntitiesRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
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
    public Task<OrganizationMember[]> GetOrganizationMembers(
        CancellationToken cancellationToken = default)
    {
        return organizationsService.GetOrganizationMembers(
            new GetOrganizationMembersRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }

    [Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
    [HttpGet("visible-users")]
    public Task<VisibleUser[]> GetVisibleUsers(
        [Microsoft.AspNetCore.Mvc.FromQuery] string? spaceKey = null,
        CancellationToken cancellationToken = default)
    {
        return organizationsService.GetVisibleUsers(
            new GetVisibleUsersRequest
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
                SpaceKey = spaceKey,
            },
            cancellationToken);
    }

    [Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
    [HttpPost("attributes")]
    public Task<long> CreateAttribute(
        [FromBody] CreateAttributeRequest request,
        CancellationToken cancellationToken = default)
    {
        return organizationsService.CreateAttribute(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }

    [Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
    [HttpPut("attributes/{id:long}")]
    public Task UpdateAttribute(
        [FromPath] long id,
        [FromBody] UpdateAttributeRequest request,
        CancellationToken cancellationToken = default)
    {
        return organizationsService.UpdateAttribute(
            request with
            {
                Id = id,
                AuthData = HttpContext.User.GetOrganizationAuthData()
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

    [Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
    [HttpDelete("attributes/{id:long}")]
    public Task DeleteAttribute(
        [FromPath] long id,
        CancellationToken cancellationToken = default)
    {
        return organizationsService.DeleteAttribute(
            new DeleteAttributeRequest
            {
                Id = id,
                AuthData = HttpContext.User.GetOrganizationAuthData()
            },
            cancellationToken);
    }
}
