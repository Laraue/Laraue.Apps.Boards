using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.Services;
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
    IOrganizationAccessService organizationAccessService,
    DatabaseContext context,
    IAccessService accessService)
    : IMovementService
{
    public async Task MoveSpace(MoveSpaceRequest request, CancellationToken cancellationToken)
    {
        await HasMassMovePermissionOrThrow(request.AuthData, cancellationToken);

        await organizationAccessService.CanCreateSpacesOrThrow(
            request.NewOrganizationId,
            request.AuthData.UserId,
            cancellationToken);
            
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await movementService.MoveSpace(request.Id, request.NewOrganizationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MoveSpaceEpics(MoveSpaceEpicsRequest request, CancellationToken cancellationToken)
    {
        await HasMassMovePermissionOrThrow(request.AuthData, cancellationToken);

        var sourceSpaceBelongsToCurrentOrganization = await context.Spaces
            .Where(x => x.Id == request.SpaceId)
            .Where(x => x.OrganizationId == request.AuthData.OrganizationId)
            .AnyAsyncEF(cancellationToken);
        
        if (!sourceSpaceBelongsToCurrentOrganization)
            throw new ForbiddenException($"Space is not exists: {request.SpaceId} in organization");

        await CanCreateEpicsOrThrow(request.AuthData.UserId, request.NewSpaceId, cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await movementService.MoveSpaceEpics(request.SpaceId, request.NewSpaceId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MoveEpic(MoveEpicRequest request, CancellationToken cancellationToken)
    {
        await HasMassMovePermissionOrThrow(request.AuthData, cancellationToken);
        
        var sourceEpicBelongsToCurrentOrganization = await context.Epics
            .Where(x => x.Id == request.Id)
            .Where(x => x.Space!.OrganizationId == request.AuthData.OrganizationId)
            .AnyAsyncEF(cancellationToken);
        
        if (!sourceEpicBelongsToCurrentOrganization)
            throw new ForbiddenException($"Epic is not exists: {request.Id} in organization");
        
        await CanCreateEpicsOrThrow(request.AuthData.UserId, request.NewSpaceId, cancellationToken);
        
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await movementService.MoveEpic(request.Id, request.NewSpaceId, cancellationToken);
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
                    Id = x.Id,
                    Color = x.Color,
                    Name = x.Name,
                })
                .ToArrayAsyncLinqToDB(cancellationToken),
            cancellationToken);
    }

    private async Task CanCreateEpicsOrThrow(
        Guid userId,
        long spaceId,
        CancellationToken cancellationToken)
    {
        var organizationId = await context.Spaces
            .Where(x => x.Id == spaceId)
            .Select(x => x.OrganizationId)
            .FirstOrThrowNotFoundEFAsync(SpaceIsNotExistsError(spaceId), cancellationToken);

        var canCreateEpicsInNewSpace = await accessService.CanCreateEpics(
            new OrganizationAuthData { OrganizationId = organizationId, UserId = userId },
            spaceId,
            cancellationToken);
        
        if (!canCreateEpicsInNewSpace)
            throw new ForbiddenException(SpaceIsNotExistsError(spaceId));
    }

    private static string SpaceIsNotExistsError(long spaceId)
    {
        return $"Space is not exists: {spaceId} or epic creation is forbidden";
    }
    
    private Task HasMassMovePermissionOrThrow(
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        return organizationAccessService.HasAccessOrThrow(
            authData,
            AdminAccessLevel.MassMove,
            cancellationToken);
    }
}

public record MoveSpaceRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public long Id { get; set; }
    public long NewOrganizationId { get; set; }
}

public record MoveSpaceEpicsRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public long SpaceId { get; set; }
    public long NewSpaceId { get; set; }
}

public record MoveEpicRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public long Id { get; set; }
    public long NewSpaceId { get; set; }
}

public record GetDestinationSpacesRequest
{
    public required OrganizationAuthData AuthData { get; set; }
    public required long OrganizationId { get; set; }
}

public record DestinationSpace
{
    public required long Id { get; set; }
    public required string Name { get; set; }
    public required string Color { get; set; }
}