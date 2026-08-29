using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.WebApiServices;

public interface IMovementService
{
    Task MoveSpace(
        MoveSpaceRequest request,
        CancellationToken cancellationToken);
    
    Task MoveSpaceEpics(
        MoveSpaceEpicsRequest request,
        CancellationToken cancellationToken);
    
    Task MoveEpic(
        MoveEpicRequest request,
        CancellationToken cancellationToken);
    
    Task<DestinationSpace[]> GetDestinationSpaces(
        GetDestinationSpacesRequest request,
        CancellationToken cancellationToken);
}

public class MovementService(
    ICoreMovementService movementService,
    DatabaseContext context,
    IAccessService accessService,
    ICoreSpacesService spacesService)
    : IMovementService
{
    public async Task MoveSpace(MoveSpaceRequest request, CancellationToken cancellationToken)
    {
        await HasMassMovePermissionOrThrow(request.AuthData, cancellationToken);
        
        var spaceId = await spacesService.GetSpaceIdBySpaceKey(
            request.AuthData.OrganizationId,
            request.Key,
            cancellationToken);

        var canCreateSpaces = await accessService.CanCreateSpaces(
            request.NewOrganizationId,
            request.AuthData.UserId,
            cancellationToken);

        if (!canCreateSpaces)
            throw new NotFoundException(string.Format(ErrorMessages.SpaceCreationForbiddenCannotMove, request.NewOrganizationId));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await movementService.MoveSpace(spaceId, request.NewOrganizationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MoveSpaceEpics(MoveSpaceEpicsRequest request, CancellationToken cancellationToken)
    {
        await HasMassMovePermissionOrThrow(request.AuthData, cancellationToken);
        
        var spaceId = await spacesService.GetSpaceIdBySpaceKey(
            request.AuthData.OrganizationId,
            request.SourceSpaceKey,
            cancellationToken);
        
        var newSpaceId = await spacesService.GetSpaceIdBySpaceKey(
            request.NewOrganizationId,
            request.NewSpaceKey,
            cancellationToken);

        if (spaceId == newSpaceId)
            throw new BadRequestException(nameof(request.NewSpaceKey), ErrorMessages.SourceDestinationSpaceSame);

        var sourceSpaceBelongsToCurrentOrganization = await context.Spaces
            .Where(x => x.Id == spaceId)
            .Where(x => x.OrganizationId == request.AuthData.OrganizationId)
            .AnyAsyncEF(cancellationToken);
        
        if (!sourceSpaceBelongsToCurrentOrganization)
            throw new ForbiddenException(string.Format(ErrorMessages.SpaceNotExistsForMove, request.SourceSpaceKey));

        await CanCreateEpicsOrThrow(request.AuthData.UserId, request.NewSpaceKey, newSpaceId, cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await movementService.MoveSpaceEpics(spaceId, newSpaceId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MoveEpic(MoveEpicRequest request, CancellationToken cancellationToken)
    {
        await HasMassMovePermissionOrThrow(request.AuthData, cancellationToken);
        
        var sourceEpicBelongsToCurrentOrganization = await context.Epics
            .Where(x => x.Id == request.SourceEpicId)
            .Where(x => x.Space!.OrganizationId == request.AuthData.OrganizationId)
            .AnyAsyncEF(cancellationToken);
        
        if (!sourceEpicBelongsToCurrentOrganization)
            throw new ForbiddenException(string.Format(ErrorMessages.EpicNotExistsForMove, request.SourceEpicId));
        
        var newSpaceId = await spacesService.GetSpaceIdBySpaceKey(
            request.NewOrganizationId,
            request.NewSpaceKey,
            cancellationToken);
        
        await CanCreateEpicsOrThrow(request.AuthData.UserId, request.NewSpaceKey, newSpaceId, cancellationToken);
        
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await movementService.MoveEpic(request.SourceEpicId, newSpaceId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<DestinationSpace[]> GetDestinationSpaces(
        GetDestinationSpacesRequest request,
        CancellationToken cancellationToken)
    {
        await HasMassMovePermissionOrThrow(request.AuthData, cancellationToken);
        
        return await accessService.GetSpacesWithAllowedEpicCreation(
            request.AuthData with { OrganizationId = request.OrganizationId },
            query => query
                .Select(x => new DestinationSpace
                {
                    Key = x.Key,
                    Color = x.Color,
                    Name = x.Name,
                })
                .ToArrayAsyncLinqToDB(cancellationToken),
            cancellationToken);
    }

    private async Task CanCreateEpicsOrThrow(
        Guid userId,
        string spaceKey,
        long spaceId,
        CancellationToken cancellationToken)
    {
        var organizationId = await context.Spaces
            .Where(x => x.Id == spaceId)
            .Select(x => x.OrganizationId)
            .FirstOrThrowNotFoundEFAsync(SpaceIsNotExistsError(spaceKey), cancellationToken);

        var canCreateEpicsInNewSpace = await accessService.CanCreateEpics(
            new OrganizationAuthData { OrganizationId = organizationId, UserId = userId },
            spaceId,
            cancellationToken);
        
        if (!canCreateEpicsInNewSpace)
            throw new ForbiddenException(SpaceIsNotExistsError(spaceKey));
    }

    private static string SpaceIsNotExistsError(string spaceKey)
    {
        return string.Format(ErrorMessages.SpaceNotExistsOrEpicCreationForbidden, spaceKey);
    }
    
    private async Task HasMassMovePermissionOrThrow(
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var hasAccess = await accessService.HasAccess(
            authData,
            AdminAccessLevel.MassMove,
            cancellationToken);

        if (!hasAccess)
            throw new NotFoundException(string.Format(ErrorMessages.EntityActionForbidden, "Organization", authData.OrganizationId, "mass move"));
    }
}

public record MoveSpaceRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public required string Key { get; set; }
    public long NewOrganizationId { get; set; }
}

public record MoveSpaceEpicsRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public required string SourceSpaceKey { get; set; }
    public required string NewSpaceKey { get; set; }
    public required long NewOrganizationId { get; set; }
}

public record MoveEpicRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public long SourceEpicId { get; set; }
    public required string NewSpaceKey { get; set; }
    public required long NewOrganizationId { get; set; }
}

public record GetDestinationSpacesRequest
{
    public required OrganizationAuthData AuthData { get; set; }
    public required long OrganizationId { get; set; }
}

public record DestinationSpace
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Color { get; set; }
}
