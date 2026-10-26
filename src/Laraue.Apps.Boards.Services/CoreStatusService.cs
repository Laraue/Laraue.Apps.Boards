using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Laraue.Apps.Boards.Services;

public interface ICoreStatusService
{
    Task<long> Create(
        CreateMessageCategoryStatusRequest request,
        CancellationToken cancellationToken);

    Task<MessageStatusDto[]> GetStatuses(
        long epicId,
        CancellationToken cancellationToken);
    
    Task Delete(
        DeleteStatusRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates a status and recalculates its epic's status. Requires a transaction.
    /// </summary>
    Task Update(
        long id,
        Action<UpdateSettersBuilder<Laraue.Apps.Boards.DataAccess.Models.Status>> setters,
        CancellationToken cancellationToken);
}

public class CoreStatusService(
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider,
    ICoreEpicStateService epicStateService)
    : ICoreStatusService
{
    public async Task<long> Create(
        CreateMessageCategoryStatusRequest request,
        CancellationToken cancellationToken)
    {
        var previousMaxOrder = await context.ActiveStatuses()
            .Where(x => x.EpicId == request.CategoryId)
            .MaxAsyncEF(x => x.SortOrder, cancellationToken);
        
        var status = new Laraue.Apps.Boards.DataAccess.Models.Status
        {
            Name = request.Name,
            EpicId = request.CategoryId,
            SortOrder = ++previousMaxOrder,
            Color = request.Color ?? Palette.DefaultStatusColor,
            Category = request.Category,
        };
        
        context.Statuses.Add(status);
        await context.SaveChangesAsync(cancellationToken);

        return status.Id;
    }

    public Task<MessageStatusDto[]> GetStatuses(
        long epicId,
        CancellationToken cancellationToken)
    {
        return context.ActiveStatuses()
            .Where(x => x.EpicId == epicId)
            .OrderBy(x => x.SortOrder)
            .Select(x => new MessageStatusDto
            {
                Id = x.Id,
                Name = x.Name,
                Color = x.Color,
                Category = x.Category,
                Count = x.Issues!.Count,
            })
            .ToArrayAsyncEF(cancellationToken);
    }

    public async Task Delete(DeleteStatusRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var categoryData = await context.ActiveStatuses()
            .Where(x => x.Id == request.Id)
            .Select(x => new { MessageCategoryId = x.EpicId })
            .FirstOrThrowNotFoundEFAsync($"Status: {request.Id} is not found", cancellationToken);

        var otherStatusExists = await context.ActiveStatuses()
            .Where(x => x.EpicId == categoryData.MessageCategoryId)
            .Where(x => x.Id != request.Id)
            .AnyAsyncEF(cancellationToken);

        if (!otherStatusExists)
            throw new BadRequestException(
                nameof(request.Id),
                "Deleting the single status in category is not allowed");

        var deletedAt = dateTimeProvider.UtcNow;

        // Deleting a status also soft-deletes its issues - consistent with how deleting a
        // Space/Epic/Organization cascades to its descendants, rather than re-pointing them to
        // a fallback status.
        await context.Issues
            .Where(x => x.StatusId == request.Id)
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.DeletedAt, deletedAt)
                .SetProperty(p => p.DeletedByUserId, request.DeleterId),
                cancellationToken);

        await context.Statuses
            .Where(x => x.Id == request.Id)
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.DeletedAt, deletedAt)
                .SetProperty(p => p.DeletedByUserId, request.DeleterId),
                cancellationToken);

        await epicStateService.RecalculateStatuses([categoryData.MessageCategoryId], cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task Update(
        long id,
        Action<UpdateSettersBuilder<Laraue.Apps.Boards.DataAccess.Models.Status>> setters,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var epicId = await context.ActiveStatuses()
            .Where(x => x.Id == id)
            .Select(x => x.EpicId)
            .FirstOrThrowNotFoundEFAsync($"Status: {id} is not found", cancellationToken);

        await context.ActiveStatuses()
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters, cancellationToken);

        // The category may have changed, which changes what every issue in the status means.
        await epicStateService.RecalculateStatuses([epicId], cancellationToken);
    }
}

public class CreateMessageCategoryStatusRequest
{
    public required string Name { get; set; }
    public required long CategoryId { get; set; }
    public string? Color { get; set; }
    public required StatusCategory Category { get; set; }
}

public class MessageStatusDto
{
    public required long Id { get; set; }
    public required string Name { get; set; }
    public required string? Color { get; set; }
    public required StatusCategory Category { get; set; }
    public required int Count { get; set; }
}

public class DeleteStatusRequest
{
    public long Id { get; set; }
    public Guid DeleterId { get; set; }
}