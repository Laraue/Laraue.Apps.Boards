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

/// <summary>
/// Every method here requires organization-level admin access (<c>EnsureAdminAccess</c>), as
/// opposed to plain organization membership handled by <see cref="IOrganizationsService"/> —
/// kept as a separate service/interface so the admin-gated surface can't accidentally be called
/// without going through an admin-authorized route (see <c>AdminOrganizationsController</c>).
/// </summary>
public interface IAdminOrganizationsService
{
    Task Update(
        EditOrganizationRequest request,
        CancellationToken cancellationToken);

    Task Delete(
        DeleteOrganizationRequest request,
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

    Task DeleteAttribute(
        DeleteAttributeRequest request,
        CancellationToken cancellationToken);
}

public class AdminOrganizationsService(
    ICoreOrganizationsService coreOrganizationsService,
    DatabaseContext context,
    IAccessService accessService)
    : IAdminOrganizationsService
{
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
                        UserId = x.UserId,
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

public record RevokeAccessRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public long OrganizationUserId { get; set; }
}

public record RegenerateJoinCodeRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
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
    public required Guid UserId { get; set; }
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
