using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.Exceptions.Web;
using Laraue.Apps.Boards.DataAccess;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Laraue.Apps.Boards.WebApiServices;

public interface ISpacesService
{
    Task<SpaceListDto[]> GetSpaces(
        GetSpacesRequest request,
        CancellationToken cancellationToken);
    
    Task<SpaceDetailsDto> GetSpace(
        GetSpaceRequest request,
        CancellationToken cancellationToken);
    
    Task<string> Create(
        CreateSpaceRequest request,
        CancellationToken cancellationToken);
    
    Task Update(
        UpdateSpaceRequest request,
        CancellationToken cancellationToken);
    
    Task Delete(
        DeleteSpaceRequest request,
        CancellationToken cancellationToken);
    
    Task<SpaceMember[]> GetMembers(
        GetSpaceMembersRequest request,
        CancellationToken cancellationToken);
}

public class SpacesService(
    ICoreSpacesService coreSpacesService,
    IAccessService accessService,
    DatabaseContext context)
    : ISpacesService
{
    public async Task<SpaceListDto[]> GetSpaces(
        GetSpacesRequest request,
        CancellationToken cancellationToken)
    {
        var spaces = await accessService.GetAvailableSpaces(
            request.AuthData,
            items => items
                .Select(x => new SpaceListDto
                {
                    Name = x.Name,
                    Color = x.Color,
                    Key = x.Key,
                    IsDefault = x.IsDefault,
                })
                .ToArrayAsyncLinqToDB(cancellationToken),
            includeDeleted: false,
            cancellationToken: cancellationToken);

        return spaces;
    }

    public async Task<SpaceDetailsDto> GetSpace(GetSpaceRequest request, CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(
            request.AuthData.OrganizationId,
            request.Key,
            cancellationToken);
        
        var spaceAccessLevel = await accessService.GetAccessLevelsBySpaceId(request.AuthData, spaceId, includeDeleted: false, cancellationToken: cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Space", request.Key));

        return new SpaceDetailsDto
        {
            CanDelete = spaceAccessLevel.CanDeleteSpace,
            CanUpdate = spaceAccessLevel.CanUpdateSpace,
            CanCreateEpics = spaceAccessLevel.CanCreateEpic,
        };
    }

    public async Task<string> Create(CreateSpaceRequest request, CancellationToken cancellationToken)
    {
        var canCreateSpaces = await accessService.CanCreateSpaces(
            request.AuthData.OrganizationId,
            request.AuthData.UserId,
            cancellationToken);

        if (!canCreateSpaces)
            throw new NotFoundException(string.Format(ErrorMessages.EntityActionForbidden, "Organization", request.AuthData.OrganizationId, "space creation"));

        var key = request.Key.ToUpper();
        await EnsureKeyIsFree(request.AuthData.OrganizationId, key, null, nameof(request.Key), cancellationToken);

        try
        {
            return await coreSpacesService.Create(
                request.AuthData.OrganizationId,
                request.AuthData.UserId,
                key,
                request.Name,
                request.Color,
                cancellationToken);
        }
        catch (Exception ex) when (IsSpaceKeyViolation(ex))
        {
            // Another request took the key between the check above and the insert.
            throw new BadRequestException(nameof(request.Key), ErrorMessages.SpaceKeyAlreadyExists);
        }
    }

    public async Task Update(UpdateSpaceRequest request, CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(
            request.AuthData.OrganizationId,
            request.OldKey,
            cancellationToken);
        
        await accessService.GetAccessLevelsBySpaceId(request.AuthData, spaceId, includeDeleted: false, cancellationToken: cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Space", request.OldKey))
            .EnsureOrThrowForbidden(a => a.CanUpdateSpace, string.Format(ErrorMessages.EntityNotAccessible, "Space", request.OldKey));

        var newKey = request.NewKey.ToUpper();
        await EnsureKeyIsFree(request.AuthData.OrganizationId, newKey, spaceId, nameof(request.NewKey), cancellationToken);

        try
        {
            await coreSpacesService.Update(
                spaceId,
                setters => setters
                    .SetProperty(x => x.Color, request.Color)
                    .SetProperty(x => x.Name, request.Name)
                    .SetProperty(x => x.Key, newKey),
                cancellationToken);
        }
        catch (Exception ex) when (IsSpaceKeyViolation(ex))
        {
            throw new BadRequestException(nameof(request.NewKey), ErrorMessages.SpaceKeyAlreadyExists);
        }
    }

    public async Task Delete(DeleteSpaceRequest request, CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(
            request.AuthData.OrganizationId,
            request.Key,
            cancellationToken);
        
        await accessService.GetAccessLevelsBySpaceId(request.AuthData, spaceId, includeDeleted: false, cancellationToken: cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Space", request.Key))
            .EnsureOrThrowForbidden(a => a.CanDeleteSpace, string.Format(ErrorMessages.EntityNotAccessible, "Space", request.Key));

        await coreSpacesService.Delete(spaceId, request.AuthData.UserId, cancellationToken);
    }

    private async Task EnsureKeyIsFree(
        long organizationId,
        string key,
        long? excludeSpaceId,
        string fieldName,
        CancellationToken cancellationToken)
    {
        var isTaken = await context.ActiveSpaces()
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => x.Key == key)
            .Where(x => x.Id != excludeSpaceId)
            .AnyAsync(cancellationToken);

        if (isTaken)
            throw new BadRequestException(fieldName, ErrorMessages.SpaceKeyAlreadyExists);
    }

    private static bool IsSpaceKeyViolation(Exception ex)
    {
        var pgException = ex as PostgresException ?? ex.InnerException as PostgresException;

        return pgException is
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_spaces_organization_id_key",
        };
    }

    public async Task<SpaceMember[]> GetMembers(GetSpaceMembersRequest request, CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(
            request.AuthData.OrganizationId,
            request.Key,
            cancellationToken);
        
        var members = await accessService.GetVisibleUsers(
            [spaceId],
            query => query
                .Select(x => new SpaceMember
                {
                    UserId = x.UserId,
                    Initials = x.Initials,
                    DisplayName = x.DisplayName,
                    Color = x.Color,
                    IsCurrentUser = x.UserId == request.AuthData.UserId,
                })
                .ToArrayAsyncEF(cancellationToken));

        return members;
    }
}

public record CreateSpaceRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    
    [MaxLength(128)]
    [MinLength(3)]
    public required string Name { get; set; }
    
    [MaxLength(7)]
    [MinLength(7)]
    public required string Color { get; set; }
    
    [MaxLength(3)]
    [MinLength(3)]
    public required string Key { get; set; }
}

public record UpdateSpaceRequest
{
    public OrganizationAuthData AuthData { get; set; }

    public string OldKey { get; set; } = string.Empty;
    
    [MaxLength(128)]
    [MinLength(3)]
    public required string Name { get; set; }
    
    [MaxLength(7)]
    [MinLength(7)]
    public required string Color { get; set; }
    
    [MaxLength(3)]
    [MinLength(3)]
    public required string NewKey { get; set; }
}

public record DeleteSpaceRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public required string Key { get; set; }
}

public record GetSpacesRequest
{
    public required OrganizationAuthData AuthData { get; set; }
}

public record SpaceListDto
{
    public required string Name { get; set; }
    public required string Color { get; set; }
    public required string Key { get; set; }
    public required bool IsDefault { get; set; }
}

public record GetSpaceRequest
{
    public required OrganizationAuthData AuthData { get; set; }
    public required string Key { get; set; }
}

public record SpaceDetailsDto
{
    public required bool CanCreateEpics { get; set; }
    public required bool CanUpdate { get; set; }
    public required bool CanDelete { get; set; }
}

public record GetSpaceMembersRequest
{
    public required OrganizationAuthData AuthData { get; set; }
    public required string Key { get; set; }
}

public record SpaceMember
{
    public required Guid UserId { get; set; }
    public required string DisplayName { get; set; }
    public required string Initials { get; set; }
    public required string Color { get; set; }
    public required bool IsCurrentUser { get; set; }
}
