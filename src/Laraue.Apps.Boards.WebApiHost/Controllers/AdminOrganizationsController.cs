using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Telegram.NET.Abstractions.Request;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

/// <summary>
/// Every action here requires organization-level admin access (checked via
/// <see cref="IOrganizationsService"/>'s <c>EnsureAdminAccess</c>, not just organization
/// membership) — kept separate from <see cref="OrganizationsController"/> so the route alone
/// tells you an action is admin-gated, and so <c>/api/organizations/members</c> stays free for
/// the non-admin "who can I filter/assign to" endpoint.
/// </summary>
[Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
[ApiController]
[Route("/api/admin/organizations")]
public class AdminOrganizationsController(IOrganizationsService organizationsService) : ControllerBase
{
    // Update/Delete organization aren't Organization-token-scoped like the rest of this
    // controller — they take the organization id from the route and run under the plain User
    // scheme, same as before the move.
    [Authorize(AuthenticationSchemes = AuthSchemas.User)]
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

    [Authorize(AuthenticationSchemes = AuthSchemas.User)]
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
