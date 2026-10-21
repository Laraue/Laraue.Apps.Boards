using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
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
    ICoreSpaceLinksService coreSpaceLinksService,
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
        
        await accessService.GetAccessLevelsBySpaceId(request.AuthData, spaceId, includeDeleted: false, cancellationToken: cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Space", request.OldKey))
            .EnsureOrThrowForbidden(a => a.CanUpdateSpace, string.Format(ErrorMessages.EntityNotAccessible, "Space", request.OldKey));

        // The settings, description and links are saved together: a failure in any of them changes nothing.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        await coreSpacesService.Update(
            spaceId,
            setters =>
            {
                setters
                    .SetProperty(x => x.Color, request.Color)
                    .SetProperty(x => x.Name, request.Name)
                    .SetProperty(x => x.Key, request.NewKey.ToUpper());
                if (request.Description is not null)
                {
                    setters.SetProperty(x => x.Description, description);
                }
            },
            cancellationToken);

        if (request.Links is not null)
        {
            await coreSpaceLinksService.Replace(
                spaceId,
                request.Links
                    .Select(x => new SpaceLinkChange(x.Id, x.Url.Trim(), x.Description.Trim()))
                    .ToList(),
                request.AuthData.UserId,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
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

    /// <summary>
    /// The space's description (markdown, BRD-312): null leaves it as it is, empty clears it.
    /// </summary>
    [MaxLength(Constraints.MaxSpaceDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>
    /// The space's links, the whole list: links with an id are kept and changed, links without one added, the others
    /// removed. Null leaves the links as they are; an empty list removes them all.
    /// </summary>
    [MaxLength(Constraints.MaxSpaceLinksPerSpace)]
    public List<SpaceLinkRequest>? Links { get; set; }
}

public record SpaceLinkRequest
{
    /// <summary>The link's id, from <c>GET /api/spaces/{key}/about</c>; null for a new link.</summary>
    public long? Id { get; init; }

    [Required]
    [Url]
    [MaxLength(Constraints.MaxSpaceLinkUrlLength)]
    public required string Url { get; init; }

    [Required]
    [MinLength(1)]
    [MaxLength(Constraints.MaxSpaceLinkDescriptionLength)]
    public required string Description { get; init; }
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
