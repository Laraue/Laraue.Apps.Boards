using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Extensions;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DataAccess.Linq2DB.Extensions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

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

    Task<ShortPaginatedResult<EpicStatusesDto>> SearchEpicsWithStatuses(
        SearchEpicStatusesRequest request,
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
        await accessService.GetAccessLevelsByEpicId(request.AuthData, request.Id, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Epic", request.Id))
            .EnsureOrThrowForbidden(a => a.CanUpdateEpic, string.Format(ErrorMessages.EntityNotAccessible, "Epic", request.Id));

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
                .FirstOrThrowNotFoundLinq2DbAsync(string.Format(ErrorMessages.EntityNotFound, "Epic", request.Id), cancellationToken),
            cancellationToken);
        
        var accessLevels = await accessService.GetAccessLevelsByEpicId(request.AuthData, request.Id, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Epic", request.Id));

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
                string.Format(ErrorMessages.SpaceNotExists, request.SpaceKey));

        var statuses = request.Statuses is { Count: > 0 }
            ? request.Statuses
                .Select(s => new Status { Name = s.Name, Color = s.Color })
                .ToArray()
            : null;

        return await coreEpicsService.Create(
            spaceId,
            request.AuthData.UserId,
            request.Name,
            request.Color,
            statuses,
            cancellationToken);
    }

    public Task<ShortPaginatedResult<EpicStatusesDto>> SearchEpicsWithStatuses(
        SearchEpicStatusesRequest request,
        CancellationToken cancellationToken)
    {
        return accessService.GetAvailableSpaces(
            request.AuthData,
            spaces =>
            {
                if (!string.IsNullOrEmpty(request.SpaceKey))
                    spaces = spaces.Where(x => x.Key == request.SpaceKey);

                var query = spaces.SelectMany(x => x.Epics!);

                if (!string.IsNullOrEmpty(request.SearchString))
                    query = query.Where(x => EF.Functions.ILike(x.Name, request.SearchString.AsSearchable()));

                return query
                    .OrderByDescending(x => x.Id)
                    .Select(x => new EpicStatusesDto
                    {
                        EpicId = x.Id,
                        EpicName = x.Name,
                        EpicColor = x.Color,
                        Statuses = x.Statuses!
                            .OrderBy(s => s.SortOrder)
                            .Select(s => new StatusDto
                            {
                                Id = s.Id,
                                Name = s.Name,
                                Color = s.Color,
                                SortOrder = s.SortOrder,
                            })
                            .ToArray(),
                    })
                    .ShortPaginateEFAsync(request.Pagination, cancellationToken);
            },
            cancellationToken);
    }

    public async Task ChangeStatusesOrder(
        ChangeStatusesOrderRequest request,
        CancellationToken cancellationToken)
    {
        await accessService.GetAccessLevelsByEpicId(request.AuthData, request.EpicId, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Epic", request.EpicId))
            .EnsureOrThrowForbidden(a => a.CanUpdateEpic, string.Format(ErrorMessages.EntityNotAccessible, "Epic", request.EpicId));

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
        await accessService.GetAccessLevelsByEpicId(request.AuthData, request.Id, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Epic", request.Id))
            .EnsureOrThrowForbidden(a => a.CanUpdateEpic, string.Format(ErrorMessages.EntityNotAccessible, "Epic", request.Id));

        await coreEpicsService.Update(
            request.Id,
            upd => upd
                .SetProperty(x => x.Color, request.Color)
                .SetProperty(x => x.Name, request.Name),
            cancellationToken);
    }

    public async Task Delete(DeleteEpicRequest request, CancellationToken cancellationToken)
    {
        await accessService.GetAccessLevelsByEpicId(request.AuthData, request.Id, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Epic", request.Id))
            .EnsureOrThrowForbidden(a => a.CanDeleteEpic, string.Format(ErrorMessages.EntityNotAccessible, "Epic", request.Id));

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

    /// <summary>
    /// Statuses to create the epic with. Null or empty means the epic is created
    /// with a single default status, same as before this field existed.
    /// </summary>
    public IReadOnlyList<CreateEpicStatusDto>? Statuses { get; set; }
}

public record CreateEpicStatusDto
{
    [MaxLength(128)]
    public required string Name { get; set; }

    [MaxLength(7)]
    public required string Color { get; set; }
}

public record SearchEpicStatusesRequest : IPaginatedRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();

    /// <summary>
    /// Restricts the search to epics of a single space. Null means all spaces
    /// in the organization the user has read access to.
    /// </summary>
    public string? SpaceKey { get; set; }

    public string? SearchString { get; set; }
    public required PaginationData Pagination { get; set; }
}

public record EpicStatusesDto
{
    public required long EpicId { get; set; }
    public required string EpicName { get; set; }
    public required string? EpicColor { get; set; }
    public required StatusDto[] Statuses { get; set; }
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
