using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Laraue.Apps.Boards.Services;

public interface ICoreEpicsService
{
    Task<long> Create(
        long spaceId,
        Guid userId,
        string name,
        string color,
        Status[]? statuses,
        CancellationToken cancellationToken);
    
    Task ChangeStatusesOrder(
        ChangeStatusesOrderRequest request,
        CancellationToken cancellationToken);
    
    Task<DeleteImpact> Delete(
        DeleteRequest request,
        CancellationToken cancellationToken);
    
    Task Update(
        long id,
        Action<UpdateSettersBuilder<Epic>> setters,
        CancellationToken cancellationToken);
}

public class CoreEpicsService(DatabaseContext context, IDateTimeProvider dateTimeProvider)
    : ICoreEpicsService
{
    public async Task<long> Create(
        long spaceId,
        Guid userId,
        string name,
        string color,
        Status[]? statuses,
        CancellationToken cancellationToken)
    {
        var dateTime = dateTimeProvider.UtcNow;
        
        var category = new Epic
        {
            Name = name,
            UserId = userId,
            Color = color,
            CreatedAt = dateTime,
            UpdatedAt = dateTime,
            TouchedAt = dateTime,
            SpaceId = spaceId,
        };
        
        statuses ??= [
            new Status
            {
                Name = IssueDefaults.DefaultStatusName,
                Color = Palette.DefaultStatusColor
            }];

        category.Statuses = statuses
            .Select((s, i) => new Laraue.Apps.Boards.DataAccess.Models.Status
            {
                SortOrder = i,
                Color = s.Color,
                Name = s.Name,
            })
            .ToList();
        
        context.Epics.Add(category);
        await context.SaveChangesAsync(cancellationToken);

        return category.Id;
    }

    public async Task ChangeStatusesOrder(
        ChangeStatusesOrderRequest request,
        CancellationToken cancellationToken)
    {
        var existsStatuses = await context.Statuses
            .Where(x => x.EpicId == request.CategoryId)
            .Select(x => new Laraue.Apps.Boards.DataAccess.Models.Status
            {
                Id = x.Id,
                EpicId = x.EpicId,
                SortOrder = x.SortOrder,
            })
            .ToArrayAsyncEF(cancellationToken);

        var existsStatusesIds = existsStatuses
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToArray();
        
        if (!existsStatusesIds.SequenceEqual(request.Order.Keys))
            throw new BadRequestException(
                nameof(request.Order),
                $"The list of statuses to update does not match the statuses in category {string.Join(',', existsStatusesIds)}");

        foreach (var status in existsStatuses)
        {
            context.Attach(status);
            status.SortOrder = request.Order[status.Id];
        }
        
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<DeleteImpact> Delete(DeleteRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var defaultEpic = await context.Epics
            .Where(x => x.Id == request.Id)
            .Select(x => x.Space!)
            .Select(o => o.Epics!.First(y => y.IsDefault))
            .Select(e => new
            {
                EpicId = e.Id,
                NewStatusId = (long?)e.Statuses!.OrderBy(o => o.SortOrder).FirstOrDefault()!.Id, // Status should be taken from FE in future iterations
            })
            .FirstOrDefaultAsyncEF(cancellationToken);

        if (defaultEpic is null)
            throw new NotFoundException($"Backlog for space with Epic:{request.Id} is not found");

        if (defaultEpic.EpicId == request.Id)
            throw new ForbiddenException("Default Epic can not be deleted");

        var affectedLinkedChats = await context.LinkedTelegramChats
            .Where(x => x.EpicId == request.Id)
            .CountAsync(cancellationToken);

        await context.Epics
            .Where(c => c.Id == request.Id)
            .DeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new DeleteImpact(affectedLinkedChats);
    }

    public Task Update(
        long id,
        Action<UpdateSettersBuilder<Epic>> setters,
        CancellationToken cancellationToken)
    {
        var date = dateTimeProvider.UtcNow;
        
        return context.Epics
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(
                update =>
                {
                    setters(update);
                    update
                        .SetProperty(p => p.UpdatedAt, date)
                        .SetProperty(p => p.TouchedAt, date);
                },
                cancellationToken);
    }
}

public class ChangeStatusesOrderRequest
{
    public required long CategoryId { get; set; }
    
    /// <summary>
    /// Order map, status id -> order value.
    /// </summary>
    public required IReadOnlyDictionary<long, int> Order { get; set; }
}

public class Status
{
    public required string Name { get; set; }
    public required string Color { get; set; }
}

public record DeleteRequest
{
    public required long Id { get; set; }
}