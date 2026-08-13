using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.Services;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Boards.WebApiServices;

public interface IStatusesService
{
    Task<long> CreateStatus(
        CreateStatusRequest request,
        CancellationToken cancellationToken);

    Task<DeleteImpact> Delete(
        DeleteStatusRequest request,
        CancellationToken cancellationToken);
    
    Task Edit(
        EditStatusRequest request,
        CancellationToken cancellationToken);
    
    Task<MessageStatusDto[]> GetStatuses(
        GetStatusesRequest request,
        CancellationToken cancellationToken);
}

public class StatusesService(
    ICoreStatusService statusService,
    IAccessService accessService)
    : IStatusesService
{
    public async Task<long> CreateStatus(
        CreateStatusRequest request,
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

        return await statusService.Create(
            new CreateMessageCategoryStatusRequest
            {
                CategoryId = request.EpicId,
                Name = request.Name,
                Color = request.Color,
            },
            cancellationToken);
    }

    public async Task<DeleteImpact> Delete(DeleteStatusRequest request, CancellationToken cancellationToken)
    {
        var canModify = await accessService.CanModifyStatus(
            request.AuthData,
            request.Id,
            cancellationToken);

        if (!canModify)
            throw new NotFoundException($"Status: {request.Id} is not found");

        return await statusService.Delete(
            new Boards.Services.DeleteStatusRequest
            {
                Id = request.Id,
            },
            cancellationToken);
    }

    public async Task Edit(EditStatusRequest request, CancellationToken cancellationToken)
    {
        var canModify = await accessService.CanModifyStatus(
            request.AuthData,
            request.Id,
            cancellationToken);
        
        if (!canModify)
            throw new NotFoundException($"Status: {request.Id} is not found");

        await statusService.Update(
            request.Id,
            upd => upd
                .SetProperty(x => x.Color, request.Color)
                .SetProperty(x => x.Name, request.Name),
            cancellationToken);
    }

    public async Task<MessageStatusDto[]> GetStatuses(GetStatusesRequest request, CancellationToken cancellationToken)
    {
        await accessService.GetAvailableEpics(
            request.AuthData,
            x => x
                .Where(e => e.Id == request.EpicId)
                .FirstOrThrowNotFoundEFAsync("Epic is not found", cancellationToken),
            cancellationToken);

        return await statusService.GetStatuses(
            request.EpicId,
            cancellationToken);
    }
}

public record CreateStatusRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    
    [MaxLength(128)]
    public required string Name { get; set; }
    
    [MaxLength(7)]
    public required string Color { get; set; }
    
    public required long EpicId { get; set; }
}

public record DeleteStatusRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public required long Id { get; set; }
}

public record EditStatusRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public long Id { get; set; }
    
    [MaxLength(7)]
    public required string Color { get; set; }
    public required string Name { get; set; }
}

public record GetStatusesRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public required long EpicId { get; set; }
}