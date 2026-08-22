using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;

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
    IAccessService accessService)
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
            cancellationToken);
        
        return spaces;
    }

    public async Task<SpaceDetailsDto> GetSpace(GetSpaceRequest request, CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(
            request.AuthData.OrganizationId,
            request.Key,
            cancellationToken);
        
        var spaceAccessLevel = await accessService.GetAccessLevelsBySpaceId(request.AuthData, spaceId, cancellationToken)
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
            throw new NotFoundException($"Organization: {request.AuthData.OrganizationId} space creation is forbidden");

        return await coreSpacesService.Create(
            request.AuthData.OrganizationId,
            request.AuthData.UserId,
            request.Key,
            request.Name,
            request.Color,
            cancellationToken);
    }

    public async Task Update(UpdateSpaceRequest request, CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(
            request.AuthData.OrganizationId,
            request.OldKey,
            cancellationToken);
        
        await accessService.GetAccessLevelsBySpaceId(request.AuthData, spaceId, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Space", request.OldKey))
            .EnsureOrThrowForbidden(a => a.CanUpdateSpace, string.Format(ErrorMessages.EntityNotAccessible, "Space", request.OldKey));

        await coreSpacesService.Update(
            spaceId,
            setters => setters
                .SetProperty(x => x.Color, request.Color)
                .SetProperty(x => x.Name, request.Name)
                .SetProperty(x => x.Key, request.NewKey.ToUpper()),
            cancellationToken);
    }

    public async Task Delete(DeleteSpaceRequest request, CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(
            request.AuthData.OrganizationId,
            request.Key,
            cancellationToken);
        
        await accessService.GetAccessLevelsBySpaceId(request.AuthData, spaceId, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Space", request.Key))
            .EnsureOrThrowForbidden(a => a.CanDeleteSpace, string.Format(ErrorMessages.EntityNotAccessible, "Space", request.Key));

        await coreSpacesService.Delete(spaceId, cancellationToken);
    }

    public async Task<SpaceMember[]> GetMembers(GetSpaceMembersRequest request, CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(
            request.AuthData.OrganizationId,
            request.Key,
            cancellationToken);
        
        var members = await accessService.GetSpaceMembers(
            request.AuthData,
            spaceId,
            query => query
                .Select(x => new SpaceMember
                {
                    UserId = x.UserId,
                    Initials = x.User!.Initials,
                    DisplayName = x.User.DisplayName,
                    Color = x.User.Color,
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
