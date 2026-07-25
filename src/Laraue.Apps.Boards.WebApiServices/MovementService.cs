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

    Task MoveIssue(
        MoveIssueRequest request,
        CancellationToken ct);

    Task SetIssueOrder(
        SetIssueOrderRequest request,
        CancellationToken ct);
}

public class MovementService(
    ICoreMovementService movementService,
    IOrganizationAccessService organizationAccessService,
    DatabaseContext context,
    IAccessService accessService,
    IIssuesService issuesService)
    : IMovementService
{
    public async Task MoveSpace(MoveSpaceRequest request, CancellationToken cancellationToken)
    {
        await HasMassMovePermissionOrThrow(request.AuthData, cancellationToken);

        await organizationAccessService.CanCreateSpacesOrThrow(
            request.NewOrganizationId,
            request.AuthData.UserId,
            cancellationToken);
            
        await movementService.MoveSpace(request.Id, request.NewOrganizationId, cancellationToken);
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

    public async Task MoveIssue(MoveIssueRequest request, CancellationToken ct)
    {
        var issueId = await issuesService.GetIssueIdByIssueKey(request.AuthData.OrganizationId, request.IssueKey, ct);
        
        // Check that can move Issue
        var accessLevels = await accessService.GetAccessLevelsByIssueId(
            request.AuthData,
            issueId,
            ct);

        if (accessLevels is null)
            throw new NotFoundException($"Issue: {request.IssueKey} is not found");

        if (!accessLevels.CanUpdateIssue)
            throw new ForbiddenException($"Issue: {request.IssueKey} is not accessible");
        
        // Check that can move to specified status
        var canMove = await accessService.CanMoveToStatus(
            request.AuthData,
            request.StatusId,
            ct);
        
        if (!canMove)
            throw new NotFoundException($"Status: {request.StatusId} is not found");
        
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await movementService.MoveIssue(
            request.AuthData.UserId,
            issueId,
            request.StatusId,
            ct);
        await transaction.CommitAsync(ct);
    }

    public Task SetIssueOrder(SetIssueOrderRequest request, CancellationToken ct)
    {
        throw new NotImplementedException();
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
    public OrganizationAuthData AuthData { get; set; } = new();
    public long Id { get; set; }
    public long NewOrganizationId { get; set; }
}

public record MoveSpaceEpicsRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public long SpaceId { get; set; }
    public long NewSpaceId { get; set; }
}

public record MoveEpicRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
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

public record MoveIssueRequest
{
    public required OrganizationAuthData AuthData { get; set; }
    public required IssueKey IssueKey { get; set; }
    public required long StatusId { get; set; }
}

public record SetIssueOrderRequest
{
    public OrganizationAuthData AuthData { get; set; }
    
    /// <summary>
    /// Issue to update order key.
    /// </summary>
    public IssueKey IssueKey { get; set; }
    
    /// <summary>
    /// Status identifier. Should be the same as the status of issue with <see cref="PreviousIssueKey"/>.
    /// </summary>
    public required long StatusId { get; set; }
    
    /// <summary>
    /// The boards card key after which the issue should appear.
    /// </summary>
    public IssueKey? PreviousIssueKey { get; set; }
}