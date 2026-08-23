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
    
    Task Update(
        EditOrganizationRequest request,
        CancellationToken cancellationToken);
    
    Task Delete(
        DeleteOrganizationRequest request,
        CancellationToken cancellationToken);
    
    Task Join(
        JoinOrganizationRequest request,
        CancellationToken cancellationToken);
    
    Task Leave(
        LeaveOrganizationRequest request,
        CancellationToken cancellationToken);
    
    Task RevokeAccess(
        RevokeAccessRequest request,
        CancellationToken cancellationToken);
    
    Task<string> RegenerateJoinCode(
        RegenerateJoinCodeRequest request,
        CancellationToken cancellationToken);
    
    Task SetUserPermissions(
        SetPermissionsRequest request,
        CancellationToken cancellationToken);
    
    Task<UserPermissions> GetUserPermissions(
        GetUserPermissionsRequest request,
        CancellationToken cancellationToken);
    
    Task<string> Login(
        LoginRequest request,
        CancellationToken cancellationToken);
    
    Task<OrganizationMember[]> GetOrganizationMembers(
        GetOrganizationMembersRequest request,
        CancellationToken cancellationToken);
    
    Task<string?> GetOrganizationJoinCode(
        GetOrganizationJoinCodeRequest request,
        CancellationToken cancellationToken);
    
    Task<PermittableSpace[]> GetPermittableEntities(
        GetPermittableEntitiesRequest request,
        CancellationToken cancellationToken);

    Task<long> CreateAttribute(
        CreateAttributeRequest request,
        CancellationToken cancellationToken);

    Task UpdateAttribute(
        UpdateAttributeRequest request,
        CancellationToken cancellationToken);

    Task<AttributeDto[]> GetAttributes(
        GetAttributesRequest request,
        CancellationToken cancellationToken);

    Task DeleteAttribute(
        DeleteAttributeRequest request,
        CancellationToken cancellationToken);
}

public class OrganizationsService(
    ICoreOrganizationsService coreOrganizationsService,
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

    public async Task Update(EditOrganizationRequest request, CancellationToken cancellationToken)
    {
        await EnsureAdminAccess(
            new OrganizationAuthData
            {
                OrganizationId = request.Id,
                UserId = request.UserId,
            },
            AdminAccessLevel.UpdateOrganization,
            "Updating organization",
            cancellationToken);

        await coreOrganizationsService.Update(
            request.Id,
            setters => setters
                .SetProperty(x => x.Color, request.Color)
                .SetProperty(x => x.Name, request.Name)
                .SetProperty(x => x.Slug, request.Slug),
            cancellationToken);
    }

    public async Task Delete(DeleteOrganizationRequest request, CancellationToken cancellationToken)
    {
        await EnsureAdminAccess(
            new OrganizationAuthData
            {
                OrganizationId = request.Id,
                UserId = request.UserId,
            },
            AdminAccessLevel.DeleteOrganization,
            "Deleting organization",
            cancellationToken);

        await coreOrganizationsService.Delete(request.Id, cancellationToken);
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
        await context.OrganizationUsers
            .Where(x => x.UserId == request.UserId)
            .Where(x => x.OrganizationId == request.OrganizationId)
            .DeleteOrThrowNotFoundLinq2DbAsync(
                "Organization is not found or user is not a participator of organization", 
                cancellationToken);
    }

    public async Task RevokeAccess(RevokeAccessRequest request, CancellationToken cancellationToken)
    {
        await EnsureAdminAccess(
            request.AuthData,
            AdminAccessLevel.Manage,
            "Revoking organization access",
            cancellationToken);

        var userData = await context.OrganizationUsers
            .Where(x => x.Id == request.OrganizationUserId)
            .Select(x => new
            {
                IsOwner = x.Organization!.OwnerId == x.UserId,
            })
            .FirstOrThrowNotFoundEFAsync(ErrorMessages.UserNotFoundInOrganization, cancellationToken);

        if (userData.IsOwner)
            throw new ForbiddenException(ErrorMessages.OwnerAccessCannotBeRevoked);

        await context.OrganizationUsers
            .Where(x => x.Id == request.OrganizationUserId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<string> RegenerateJoinCode(RegenerateJoinCodeRequest request, CancellationToken cancellationToken)
    {
        await EnsureAdminAccess(
            request.AuthData,
            AdminAccessLevel.Manage,
            "Regenerating organization join code",
            cancellationToken);

        var newCode = StringGenerator.GenerateJoinCode();
        await context.Organizations
            .Where(x => x.Id == request.AuthData.OrganizationId)
            .ExecuteUpdateAsync(u => u
                    .SetProperty(p => p.JoinCode, newCode),
                cancellationToken);
        
        return newCode;
    }

    public async Task SetUserPermissions(SetPermissionsRequest request, CancellationToken cancellationToken)
    {
        await EnsureAdminAccess(
            request.AuthData,
            AdminAccessLevel.Manage,
            "Setting user permissions",
            cancellationToken);

        await context.OrganizationUsers
            .Where(x => x.Id == request.OrganizationUserId)
            .AnyOrThrowNotFoundEFAsync(
                x => x.OrganizationId == request.AuthData.OrganizationId,
                string.Format(ErrorMessages.EntityNotFound, "OrganizationUser", request.OrganizationUserId), cancellationToken);

        // Check that passed spaces belongs to organization
        if (request.UserPermissions.Direct.Count > 0)
        {
            var permittableEntities = (await coreOrganizationsService.GetPermittableEntities(
                request.AuthData.OrganizationId,
                cancellationToken))
                .ToDictionary(
                    x => x.Key,
                    x => new { Self = x });

            var errors = new List<string>();
            
            foreach (var directSpacePermission in request.UserPermissions.Direct)
            {
                if (!permittableEntities.TryGetValue(directSpacePermission.Key, out var space))
                {
                    errors.Add(string.Format(ErrorMessages.SpaceDirectPermissionEntityNotFound, directSpacePermission.Key));
                    continue;
                }

                if (space.Self.IsDefault && directSpacePermission.Value.CanDelete)
                    errors.Add(string.Format(ErrorMessages.SpaceDeletePermissionOnDefaultForbidden, directSpacePermission.Key));
            }

            if (errors.Count != 0)
            {
                throw new BadRequestException(
                    new Dictionary<string, string?[]>
                    {
                        [nameof(UserPermissions.Direct)] = errors.ToArray(),
                    });
            }
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        
        await coreOrganizationsService.SetUserPermissions(
            request.OrganizationUserId,
            request.UserPermissions,
            cancellationToken);
        
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<UserPermissions> GetUserPermissions(GetUserPermissionsRequest request, CancellationToken cancellationToken)
    {
        await EnsureAdminAccess(
            request.AuthData,
            AdminAccessLevel.Manage,
            "Reading user permissions",
            cancellationToken);

        await context.OrganizationUsers
            .Where(x => x.Id == request.OrganizationUserId)
            .AnyOrThrowNotFoundEFAsync(
                x => x.OrganizationId == request.AuthData.OrganizationId,
                string.Format(ErrorMessages.EntityNotFound, "OrganizationUser", request.OrganizationUserId), cancellationToken);

        return await coreOrganizationsService.GetUserPermissions(
            request.OrganizationUserId,
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

    public async Task<OrganizationMember[]> GetOrganizationMembers(
        GetOrganizationMembersRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAccess(
            request.AuthData,
            AdminAccessLevel.Manage,
            "Listing organization members",
            cancellationToken);

        var data = await accessService.GetOrganizationMembers(
            request.AuthData.OrganizationId,
            query =>
            {
                return query
                    .Select(x => new OrganizationMember
                    {
                        Color = x.User!.Color,
                        DisplayName = x.User.DisplayName,
                        Initials = x.User.Initials,
                        OrganizationUserId = x.Id,
                        IsOwner = x.Organization!.OwnerId == x.UserId,
                        AdminAccessLevel = x.AdminAccessLevel,
                    })
                    .ToArrayAsyncEF(cancellationToken);
            });

        return data;
    }

    public async Task<string?> GetOrganizationJoinCode(GetOrganizationJoinCodeRequest request, CancellationToken cancellationToken)
    {
        await EnsureAdminAccess(
            request.AuthData,
            AdminAccessLevel.Manage,
            "Reading organization join code",
            cancellationToken);

        return await context.Organizations
            .Where(o => o.Id == request.AuthData.OrganizationId)
            .Select(x => x.JoinCode)
            .FirstOrDefaultAsyncEF(cancellationToken);
    }

    public async Task<PermittableSpace[]> GetPermittableEntities(
        GetPermittableEntitiesRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAccess(
            request.AuthData,
            AdminAccessLevel.Manage,
            "Listing permittable entities",
            cancellationToken);

        return await coreOrganizationsService.GetPermittableEntities(
            request.AuthData.OrganizationId,
            cancellationToken);
    }

    public async Task<long> CreateAttribute(CreateAttributeRequest request, CancellationToken cancellationToken)
    {
        await EnsureAdminAccess(
            request.AuthData,
            AdminAccessLevel.ManageAttributes,
            "Creating organization attribute",
            cancellationToken);

        if (request is { Type: AttributeType.List, ListValues.Length: < 1 })
            throw new BadRequestException(
                nameof(request.ListValues),
                ErrorMessages.ListAttributeRequiresOptions);

        if (request is { Type: not AttributeType.List, ListValues.Length: > 0 })
            throw new BadRequestException(
                nameof(request.ListValues),
                ErrorMessages.OnlyListAttributeHasOptions);

        return await coreOrganizationsService.CreateAttribute(
            request.AuthData.OrganizationId,
            request.Name,
            request.Color,
            request.Type,
            request.ListValues?.Select(x => x.Name).ToArray(),
            cancellationToken);
    }

    public async Task UpdateAttribute(UpdateAttributeRequest request, CancellationToken cancellationToken)
    {
        await EnsureAdminAccess(
            request.AuthData,
            AdminAccessLevel.ManageAttributes,
            "Updating organization attribute",
            cancellationToken);

        await EnsureAttributeExists(request.AuthData.OrganizationId, request.Id, cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await coreOrganizationsService.UpdateAttribute(
            request.Id,
            request.Name,
            request.Color,
            request.ListValues?
                .Select(x => new UpdateAttributeListValueRequest
                {
                    Name = x.Name,
                    Id = x.Id,
                }).ToArray(),
            cancellationToken);
        
        await transaction.CommitAsync(cancellationToken);
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

    public async Task DeleteAttribute(DeleteAttributeRequest request, CancellationToken cancellationToken)
    {
        await EnsureAdminAccess(
            request.AuthData,
            AdminAccessLevel.ManageAttributes,
            "Deleting organization attribute",
            cancellationToken);

        await EnsureAttributeExists(request.AuthData.OrganizationId, request.Id, cancellationToken);

        await coreOrganizationsService.DeleteAttribute(request.Id, cancellationToken);
    }

    private async Task EnsureAttributeExists(long organizationId, long attributeId, CancellationToken cancellationToken)
    {
        var attributeExists = await context.Attributes
            .Where(x => x.Id == attributeId)
            .AnyAsyncEF(x => x.OrganizationId == organizationId, cancellationToken);

        if (!attributeExists)
            throw new NotFoundException(string.Format(ErrorMessages.EntityNotFound, "Attribute", attributeId));
    }

    private async Task EnsureAdminAccess(
        OrganizationAuthData authData,
        AdminAccessLevel accessLevel,
        string action,
        CancellationToken cancellationToken)
    {
        var hasAccess = await accessService.HasAccess(authData, accessLevel, cancellationToken);

        if (!hasAccess)
            throw new NotFoundException(
                string.Format(ErrorMessages.AdminAccessRequired, authData.OrganizationId, action, accessLevel));
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

public record EditOrganizationRequest
{
    public long Id { get; set; }
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

public record DeleteOrganizationRequest
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
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

public record RevokeAccessRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public long OrganizationUserId { get; set; }
}

public record RegenerateJoinCodeRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
}

public record LeaveOrganizationRequest
{
    public Guid UserId { get; set; }
    public long OrganizationId { get; set; }
}

public record SetPermissionsRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public long OrganizationUserId { get; set; }
    public required UserPermissions UserPermissions { get; set; }
}

public record GetUserPermissionsRequest
{
    public OrganizationAuthData AuthData { get; set; } = new ();
    public long OrganizationUserId { get; set; }
}

public record LoginRequest
{
    public Guid UserId { get; set; }
    public long OrganizationId { get; set; }
}

public record GetOrganizationMembersRequest
{
    public required OrganizationAuthData AuthData { get; set; }
}

public record GetOrganizationJoinCodeRequest
{
    public required OrganizationAuthData AuthData { get; set; }
}

public record OrganizationMember
{
    public long OrganizationUserId { get; set; }
    public required string DisplayName { get; set; }
    public required string Initials { get; set; }
    public required string Color { get; set; }
    public required bool IsOwner { get; set; }
    public required AdminAccessLevel AdminAccessLevel { get; set; }
}

public record GetPermittableEntitiesRequest
{
    public required OrganizationAuthData AuthData { get; set; }
}

public record CreateAttributeRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    
    [MaxLength(64)]
    public required string Name { get; set; }
    
    [MinLength(7)]
    [MaxLength(7)]
    public required string Color { get; set; }
    
    public required AttributeType Type { get; set; }
    
    public NewAttributeListValueDto[]? ListValues { get; set; }
}

public record UpdateAttributeRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();

    public long Id { get; set; }
    
    [MaxLength(64)]
    public required string Name { get; set; }
    
    [MinLength(7)]
    [MaxLength(7)]
    public required string Color { get; set; }
    
    public required UpdateAttributeListValueDto[]? ListValues { get; set; }
}

public record DeleteAttributeRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();

    public long Id { get; set; }
}

public record NewAttributeListValueDto
{
    [MaxLength(64)]
    public required string Name { get; set; }
}

public record UpdateAttributeListValueDto : NewAttributeListValueDto
{
    public long? Id { get; set; }
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
