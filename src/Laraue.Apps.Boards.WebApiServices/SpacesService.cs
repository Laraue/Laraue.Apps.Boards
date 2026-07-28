using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
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
    IAccessService accessService,
    IOrganizationAccessService organizationAccessService,
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
            cancellationToken);
        
        return spaces;
    }

    public async Task<SpaceDetailsDto> GetSpace(GetSpaceRequest request, CancellationToken cancellationToken)
    {
        var spaceId = await GetSpaceIdBySpaceKey(request.AuthData, request.Key);
        
        var spaceAccessLevel = await accessService
            .GetAccessLevelsBySpaceId(request.AuthData, spaceId, cancellationToken);
        
        if (spaceAccessLevel is null)
            throw new NotFoundException($"Space: {request.Key} is not found");
        
        return new SpaceDetailsDto
        {
            CanDelete = spaceAccessLevel.CanDeleteSpace,
            CanUpdate = spaceAccessLevel.CanUpdateSpace,
            CanCreateEpics = spaceAccessLevel.CanCreateEpic,
        };
    }

    public async Task<string> Create(CreateSpaceRequest request, CancellationToken cancellationToken)
    {
        await organizationAccessService.CanCreateSpacesOrThrow(
            request.AuthData.OrganizationId,
            request.AuthData.UserId,
            cancellationToken);

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
        var accessLevel = await accessService.GetAccessLevelsBySpaceId(
            request.AuthData,
            request.Id,
            cancellationToken);

        if (accessLevel is null)
            throw new NotFoundException($"Space: {request.Id} is not found");
        
        if (!accessLevel.CanUpdateSpace)
            throw new ForbiddenException($"Space: {request.Id} is not accessible");

        await coreSpacesService.Update(
            request.Id,
            setters => setters
                .SetProperty(x => x.Color, request.Color)
                .SetProperty(x => x.Name, request.Name)
                .SetProperty(x => x.Key, request.Key.ToUpper()),
            cancellationToken);
    }

    public async Task Delete(DeleteSpaceRequest request, CancellationToken cancellationToken)
    {
        var accessLevel = await accessService.GetAccessLevelsBySpaceId(
            request.AuthData,
            request.Id,
            cancellationToken);

        if (accessLevel is null)
            throw new NotFoundException($"Space: {request.Id} is not found");
        
        if (!accessLevel.CanDeleteSpace)
            throw new ForbiddenException($"Space: {request.Id} is not accessible");
        
        await coreSpacesService.Delete(request.Id, cancellationToken);
    }

    public async Task<SpaceMember[]> GetMembers(GetSpaceMembersRequest request, CancellationToken cancellationToken)
    {
        var spaceId = await GetSpaceIdBySpaceKey(request.AuthData, request.Key);
        
        var members = await accessService.GetSpaceMembers(
            request.AuthData,
            spaceId,
            query => query
                .Select(x => new
                {
                    x.UserId,
                    x.User!.TelegramUserName,
                    x.User.TelegramFirstName,
                    x.User.TelegramLastName,
                    x.User.Color,
                })
                .ToArrayAsyncEF(cancellationToken));

        var result = new List<SpaceMember>();
        
        foreach (var member in members)
        {
            var initials = UserInitialsUtility.GetInitials(
                member.TelegramUserName,
                member.TelegramFirstName,
                member.TelegramLastName);
            
            result.Add(new SpaceMember
            {
                UserId = member.UserId,
                Initials = initials.Initials,
                DisplayName = initials.DisplayName,
                Color = member.Color,
            });
        }
        
        return result.ToArray();
    }

    private Task<long> GetSpaceIdBySpaceKey(OrganizationAuthData authData, string spaceKey)
    {
        return context.Spaces
            .Where(x => x.OrganizationId == authData.OrganizationId)
            .Where(x => x.Key == spaceKey)
            .Select(x => x.Id)
            .FirstOrThrowNotFoundEFAsync($"Space: {spaceKey} is not found");
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
    public OrganizationAuthData AuthData { get; set; } = new();
    
    public long Id { get; set; }
    
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

public record DeleteSpaceRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public long Id { get; set; }
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
}
