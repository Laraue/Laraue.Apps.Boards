using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DataAccess.Linq2DB.Extensions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.WebApiServices;

public interface IOrganizationsService
{
    Task<OrganizationListDto[]> GetOrganizations(
        GetOrganizationsRequest request,
        CancellationToken cancellationToken);

    Task<OrganizationDto> GetOrganization(
        GetOrganizationRequest request,
        CancellationToken cancellationToken);

    Task<CreateOrganizationResponse> Create(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken);

    Task Join(
        JoinOrganizationRequest request,
        CancellationToken cancellationToken);

    Task Leave(
        LeaveOrganizationRequest request,
        CancellationToken cancellationToken);

    Task<string> Login(
        LoginRequest request,
        CancellationToken cancellationToken);

    Task<VisibleUser[]> GetMembers(
        GetMembersRequest request,
        CancellationToken cancellationToken);

    Task<AttributeDto[]> GetAttributes(
        GetAttributesRequest request,
        CancellationToken cancellationToken);
}

public class OrganizationsService(
    ICoreOrganizationsService coreOrganizationsService,
    ICoreSpacesService coreSpacesService,
    DatabaseContext context,
    IAuthService authService,
    IAccessService accessService)
    : IOrganizationsService
{
    public async Task<OrganizationListDto[]> GetOrganizations(
        GetOrganizationsRequest request,
        CancellationToken cancellationToken)
    {
        var allOrganizations = await accessService.GetOrganizations(
            request.UserId,
            organizationUsers => organizationUsers
                .OrderByDescending(x => x.Organization!.Type)
                .ThenBy(x => x.Organization!.Name)
                .Select(x => new OrganizationListDto
                {
                    Id = x.Organization!.Id,
                    CanUpdate = x.AdminAccessLevel.HasFlag(AdminAccessLevel.UpdateOrganization),
                    CanDelete = x.Organization.Type != OrganizationType.Personal &&
                                x.AdminAccessLevel.HasFlag(AdminAccessLevel.DeleteOrganization),
                    CanLeave = x.Organization.OwnerId != x.UserId,
                    Name = x.Organization.Name,
                    Color = x.Organization.Color,
                    IsPersonal = x.Organization.Type == OrganizationType.Personal,
                    CanCreateSpaces = x.CanCreateSpaces,
                    Slug = x.Organization.Slug,
                    SlugPostfix = x.Organization.SlugPostfix,
                })
                .ToListAsyncEF(cancellationToken));

        return allOrganizations.ToArray();
    }

    public async Task<OrganizationDto> GetOrganization(GetOrganizationRequest request, CancellationToken cancellationToken)
    {
        var organization = await accessService.GetOrganizations(
            request.AuthData.UserId,
            organizations => organizations
                .Where(o => o.OrganizationId == request.AuthData.OrganizationId)
                .Select(x => new OrganizationDto
                {
                    Id = x.Organization!.Id,
                    CanCreateSpaces = x.CanCreateSpaces,
                    Name = x.Organization.Name,
                    Color = x.Organization.Color,
                    CanManage = x.AdminAccessLevel.HasFlag(AdminAccessLevel.Manage),
                    CanMassMove = x.AdminAccessLevel.HasFlag(AdminAccessLevel.MassMove),
                    CanManageAttributes = x.AdminAccessLevel.HasFlag(AdminAccessLevel.ManageAttributes),
                    Slug = x.Organization.Slug,
                    SlugPostfix = x.Organization.SlugPostfix,
                })
                .FirstOrThrowNotFoundEFAsync($"Organization: {request.AuthData.OrganizationId} is not found", cancellationToken));

        organization.Preferences = await coreOrganizationsService.GetPreferences(
            request.AuthData.OrganizationId,
            request.AuthData.UserId,
            cancellationToken);

        return organization;
    }

    public Task<CreateOrganizationResponse> Create(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        return coreOrganizationsService.Create(
            request.UserId,
            request.Slug,
            request.Name,
            request.Color,
            cancellationToken);
    }

    public async Task Join(JoinOrganizationRequest request, CancellationToken cancellationToken)
    {
        var organizationId = await coreOrganizationsService.GetOrganizationIdByJoinCode(
            request.JoinCode,
            cancellationToken);

        if (organizationId == null)
            throw new NotFoundException(string.Format(ErrorMessages.EntityNotFound, "Organization code", request.JoinCode));

        if (await coreOrganizationsService.HasMember(
            organizationId.Value,
            request.UserId,
            cancellationToken))
            throw new NotAcceptableException(ErrorMessages.AlreadyOrganizationMember);

        await coreOrganizationsService.AddMember(
            organizationId.Value,
            request.UserId,
            cancellationToken);
    }

    public async Task Leave(LeaveOrganizationRequest request, CancellationToken cancellationToken)
    {
        var isOwner = await context.OrganizationUsers
            .Where(x => x.UserId == request.UserId)
            .Where(x => x.OrganizationId == request.OrganizationId)
            .AnyAsync(x => x.Organization!.OwnerId == x.UserId, cancellationToken);

        if (isOwner)
            throw new ForbiddenException(ErrorMessages.OwnerAccessCannotBeRevoked);

        await context.OrganizationUsers
            .Where(x => x.UserId == request.UserId)
            .Where(x => x.OrganizationId == request.OrganizationId)
            .DeleteOrThrowNotFoundLinq2DbAsync(
                "Organization is not found or user is not a participator of organization",
                cancellationToken);
    }

    public async Task<string> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        await accessService.GetOrganizations(
            request.UserId,
            organizations => organizations
                .Where(o => o.UserId == request.UserId)
                .FirstOrThrowNotFoundEFAsync(
                    "Organization is not exists or user does not belong to organization",
                    cancellationToken));

        return authService.CreateOrganizationToken(request.OrganizationId, request.UserId);
    }

    public async Task<VisibleUser[]> GetMembers(
        GetMembersRequest request,
        CancellationToken cancellationToken)
    {
        long[] spaceIds;
        if (request.SpaceKey is not null)
        {
            var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(
                request.AuthData.OrganizationId,
                request.SpaceKey,
                cancellationToken);

            spaceIds = [spaceId];
        }
        else
        {
            spaceIds = await accessService.GetAvailableSpaces(
                request.AuthData,
                query => query.Select(s => s.Id).ToArrayAsyncEF(cancellationToken),
                cancellationToken);
        }

        return await accessService.GetVisibleUsers(
            spaceIds,
            query => query
                .Select(x => new VisibleUser
                {
                    UserId = x.UserId,
                    Initials = x.User!.Initials,
                    DisplayName = x.User.DisplayName,
                    Color = x.User.Color,
                    IsCurrentUser = x.UserId == request.AuthData.UserId,
                })
                .ToArrayAsyncEF(cancellationToken));
    }

    public async Task<AttributeDto[]> GetAttributes(GetAttributesRequest request, CancellationToken cancellationToken)
    {
        var result = await context.Attributes
            .Where(x => x.OrganizationId == request.AuthData.OrganizationId)
            .Select(x => new AttributeDto
            {
                Type = x.AttributeType,
                Color = x.Color,
                Name = x.Name,
                Id = x.Id,
                ListValues = x.AttributeListValues!
                    .Select(v => new AttributeListValueDto
                    {
                        Name = v.Value,
                        Id = v.Id,
                    })
                    .ToArray(),
            })
            .OrderBy(x => x.Id)
            .ToArrayAsync(cancellationToken);

        return result;
    }
}

public record CreateOrganizationRequest
{
    public Guid UserId { get; set; }

    [MaxLength(128)]
    [MinLength(3)]
    public required string Name { get; set; }

    [MaxLength(64)]
    [MinLength(3)]
    [RegularExpression("[A-z]*")]
    public required string Slug { get; set; }

    [MaxLength(7)]
    [MinLength(7)]
    public required string Color { get; set; }
}

public record GetOrganizationsRequest
{
    public Guid UserId { get; set; }
}

public record GetOrganizationRequest
{
    public required OrganizationAuthData AuthData { get; set; }
}

public record OrganizationListDto
{
    public required long Id { get; set; }
    public required string Name { get; set; }
    public required string? Color { get; set; }
    public required bool CanUpdate { get; set; }
    public required bool CanDelete { get; set; }
    public required bool CanLeave { get; set; }
    public required bool IsPersonal { get; set; }
    public required bool CanCreateSpaces { get; set; }
    public required string Slug { get; set; }
    public required string SlugPostfix { get; set; }
}

public record OrganizationDto
{
    public required long Id { get; set; }
    public required string Name { get; set; }
    public required string? Color { get; set; }
    public required bool CanCreateSpaces { get; set; }
    public required bool CanMassMove { get; set; }
    public required bool CanManage { get; set; }
    public required bool CanManageAttributes { get; set; }
    public required string Slug { get; set; }
    public required string SlugPostfix { get; set; }
    public UserOrganizationPreferencesResponse Preferences { get; set; } = new();
}

public record JoinOrganizationRequest
{
    public Guid UserId { get; set; }
    public required string JoinCode { get; set; }
}

public record LeaveOrganizationRequest
{
    public Guid UserId { get; set; }
    public long OrganizationId { get; set; }
}

public record LoginRequest
{
    public Guid UserId { get; set; }
    public long OrganizationId { get; set; }
}

public record GetMembersRequest
{
    public required OrganizationAuthData AuthData { get; set; }

    /// <summary>
    /// Narrows candidates to members of this one space. When omitted, every space the
    /// requesting user can read is in scope.
    /// </summary>
    public string? SpaceKey { get; set; }
}

public record VisibleUser
{
    public required Guid UserId { get; set; }
    public required string DisplayName { get; set; }
    public required string Initials { get; set; }
    public required string Color { get; set; }
    public required bool IsCurrentUser { get; set; }
}

public record GetAttributesRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
}

public record AttributeDto
{
    public required long Id { get; set; }
    public required string Name { get; set; }
    public required string Color { get; set; }
    public required AttributeType Type { get; set; }
    public required AttributeListValueDto[] ListValues { get; set; }
}

public record AttributeListValueDto
{
    public required long Id { get; set; }
    public required string Name { get; set; }
}
