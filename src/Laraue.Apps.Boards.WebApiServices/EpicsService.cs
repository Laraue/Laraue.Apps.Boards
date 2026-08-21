using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.Services;
using Laraue.Core.DataAccess.Linq2DB.Extensions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.WebApiServices;

public interface IEpicsService
{
    Task<EpicListDto[]> GetSpaceEpics(
        GetEpicsRequest request,
        CancellationToken cancellationToken);
    
    Task<EpicDto> GetEpic(
        GetEpicRequest request,
        CancellationToken cancellationToken);

    Task ChangeStatus(
        ChangeEpicStatusRequest request,
        CancellationToken cancellationToken);

    Task ChangeStatusesOrder(
        ChangeStatusesOrderRequest request,
        CancellationToken cancellationToken);
    
    Task<long> Create(
        CreateEpicRequest request,
        CancellationToken cancellationToken);
    
    Task Update(
        UpdateEpicRequest request,
        CancellationToken cancellationToken);
    
    Task Delete(
        DeleteEpicRequest request,
        CancellationToken cancellationToken);
}

public class EpicsService(
    ICoreEpicsService coreEpicsService,
    IAccessService accessService,
    ICoreSpacesService coreSpacesService)
    : IEpicsService
{
    public async Task<EpicListDto[]> GetSpaceEpics(
        GetEpicsRequest request,
        CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(
            request.AuthData.OrganizationId,
            request.Key,
            cancellationToken);
        
        return await accessService.GetAvailableEpics(
            request.AuthData,
            epics => epics
                .Where(x => x.SpaceId == spaceId)
                .Where(x => request.Statuses == null || ((IEnumerable<EpicStatus>)request.Statuses).Contains(x.Status))
                .OrderBy(x => x.IsDefault ? 0 : 1)
                .ThenBy(x => x.Name)
                .Select(x => new EpicListDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Color = x.Color,
                    TouchedAt = x.TouchedAt,
                    IsDefault = x.IsDefault,
                    Status = x.Status,
                })
                .ToArrayAsyncLinqToDB(cancellationToken),
            cancellationToken);
    }

    public async Task ChangeStatus(
        ChangeEpicStatusRequest request,
        CancellationToken cancellationToken)
    {
        var epicsAccessLevel = await accessService
            .GetAccessLevelsByEpicId(
                request.AuthData,
                request.Id,
                cancellationToken);

        if (epicsAccessLevel is null)
            throw new NotFoundException($"Epic: {request.Id} is not found");

        if (!epicsAccessLevel.CanUpdateEpic)
            throw new ForbiddenException($"Epic: {request.Id} is not accessible");

        await coreEpicsService.Update(
            request.Id,
            upd => upd.SetProperty(x => x.Status, request.Status),
            cancellationToken);
    }

    public async Task<EpicDto> GetEpic(
        GetEpicRequest request,
        CancellationToken cancellationToken)
    {
        var epicData = await accessService.GetAvailableEpics(
            request.AuthData,
            epics => epics
                .Where(x => x.Id == request.Id)
                .Select(x => new
                {
                    x.Color,
                    x.Name,
                    Statuses = x.Statuses!
                        .Select(s => new StatusDto
                        {
                            Id = s.Id,
                            Color = s.Color,
                            Name = s.Name,
                            SortOrder = s.SortOrder,
                        })
                        .ToArray(),
                    x.IsDefault,
                    x.Status,
                })
                .FirstOrThrowNotFoundLinq2DbAsync($"Epic: {request.Id} is not found", cancellationToken),
            cancellationToken);
        
        var accessLevels = await accessService.GetAccessLevelsByEpicId(
            request.AuthData,
            request.Id,
            cancellationToken);

        if (accessLevels is null)
            throw new NotFoundException($"Epic: {request.Id} is not found");

        var result = new EpicDto
        {
            CanDeleteIssues = accessLevels.CanDeleteIssue,
            CanUpdateIssues = accessLevels.CanUpdateIssue,
            CanCreateIssues = accessLevels.CanCreateIssue,
            Color = epicData.Color,
            Name = epicData.Name,
            Statuses = epicData.Statuses,
            CanDelete = accessLevels.CanDeleteEpic,
            CanUpdate = accessLevels.CanUpdateEpic,
            Status = epicData.Status,
        };
        
        return result;
    }

    public async Task<long> Create(
        CreateEpicRequest request,
        CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(
            request.AuthData.OrganizationId,
            request.SpaceKey,
            cancellationToken);

        if (!await accessService
            .CanCreateEpics(
                request.AuthData,
                spaceId,
                cancellationToken))
            throw new NotFoundException(
                $"Space: {request.SpaceKey} is not exists");
        
        return await coreEpicsService.Create(
            spaceId,
            request.AuthData.UserId,
            request.Name,
            request.Color,
            statuses: null,
            cancellationToken);
    }

    public async Task ChangeStatusesOrder(
        ChangeStatusesOrderRequest request,
        CancellationToken cancellationToken)
    {
        var epicsAccessLevel = await accessService
            .GetAccessLevelsByEpicId(
                request.AuthData,
                request.EpicId,
                cancellationToken);
        
        if (epicsAccessLevel is null)
            throw new NotFoundException($"Epic: {request.EpicId} is not found");

        if (!epicsAccessLevel.CanUpdateEpic)
            throw new ForbiddenException($"Epic: {request.EpicId} is not accessible");
        
        await coreEpicsService.ChangeStatusesOrder(
            new Boards.Services.ChangeStatusesOrderRequest
            {
                CategoryId = request.EpicId,
                Order = request.Order
            },
            cancellationToken);
    }

    public async Task Update(UpdateEpicRequest request, CancellationToken cancellationToken)
    {
        var epicsAccessLevel = await accessService
            .GetAccessLevelsByEpicId(
                request.AuthData,
                request.Id,
                cancellationToken);
        
        if (epicsAccessLevel is null)
            throw new NotFoundException($"Epic: {request.Id} is not found");

        if (!epicsAccessLevel.CanUpdateEpic)
            throw new ForbiddenException($"Epic: {request.Id} is not accessible");

        await coreEpicsService.Update(
            request.Id,
            upd => upd
                .SetProperty(x => x.Color, request.Color)
                .SetProperty(x => x.Name, request.Name),
            cancellationToken);
    }

    public async Task Delete(DeleteEpicRequest request, CancellationToken cancellationToken)
    {
        var epicsAccessLevel = await accessService
            .GetAccessLevelsByEpicId(
                request.AuthData,
                request.Id,
                cancellationToken);
        
        if (epicsAccessLevel is null)
            throw new NotFoundException($"Epic: {request.Id} is not found");

        if (!epicsAccessLevel.CanDeleteEpic)
            throw new ForbiddenException($"Epic: {request.Id} is not accessible");
        
        await coreEpicsService.Delete(
            new DeleteRequest { Id = request.Id },
            cancellationToken);
    }
}

public record EpicListDto
{
    public required long Id { get; set; }
    public required string Name { get; set; }
    public required string? Color { get; set; }
    public required DateTime TouchedAt { get; set; }
    public required bool IsDefault { get; set; }
    public required EpicStatus Status { get; set; }
}

public record GetEpicRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public required long Id { get; set; }
}

public record EpicDto
{
    public required string Name { get; set; }
    public required string? Color { get; set; }
    public StatusDto[] Statuses { get; set; } = [];
    public required bool CanCreateIssues { get; set; }
    public required bool CanDeleteIssues { get; set; }
    public required bool CanUpdateIssues { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }
    public required EpicStatus Status { get; set; }
}

public class StatusDto
{
    public required long Id { get; set; }
    public required string Name { get; set; }
    public required string? Color { get; set; }
    public required int SortOrder { get; set; }
}

public record CreateEpicRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    
    public required string SpaceKey { get; set; }
    
    [MaxLength(128)]
    public required string Name { get; set; }
    
    [MaxLength(7)]
    public required string Color { get; set; }
}

public record ChangeStatusesOrderRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public required long EpicId { get; set; }
    public required IReadOnlyDictionary<long, int> Order { get; set; }
}

public record UpdateEpicRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    
    public long Id { get; set; }
    
    [MaxLength(128)]
    public required string Name { get; set; }
    
    [MaxLength(7)]
    public required string Color { get; set; }
}

public record DeleteEpicRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();

    public long Id { get; set; }
}

public record GetEpicsRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public required string Key { get; set; }

    /// <summary>
    /// Statuses to filter epics by. Null means no filtering.
    /// </summary>
    public EpicStatus[]? Statuses { get; set; }
}

public record ChangeEpicStatusRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();

    public long Id { get; set; }

    public required EpicStatus Status { get; set; }
}
