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

    /// <summary>
    /// Makes <paramref name="statuses"/> the epic's statuses, in that order: one with an id is updated, one without is
    /// added, and the epic's other statuses are soft-deleted with their issues, as <see cref="Delete"/> does.
    /// <see cref="BadRequestException"/> when the list is empty, or an id isn't one of the epic's statuses or is
    /// listed twice. Requires a transaction.
    /// </summary>
    Task ReplaceForEpic(
        long epicId,
        IReadOnlyList<EpicStatusChange> statuses,
        Guid deleterId,
        CancellationToken cancellationToken);
}

public sealed record EpicStatusChange(long? Id, string Name, string? Color, StatusCategory Category);

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

    public async Task ReplaceForEpic(
        long epicId,
        IReadOnlyList<EpicStatusChange> statuses,
        Guid deleterId,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var existingIds = await context.ActiveStatuses()
            .Where(x => x.EpicId == epicId)
            .Select(x => x.Id)
            .ToArrayAsyncEF(cancellationToken);
        var existing = existingIds.ToHashSet();

        var errors = new Dictionary<string, string?[]>();
        if (statuses.Count == 0)
        {
            errors[nameof(statuses)] = ["A board needs at least one column"];
        }

        var seen = new HashSet<long>();
        for (var i = 0; i < statuses.Count; i++)
        {
            if (statuses[i].Id is not { } id)
                continue;
            if (!existing.Contains(id))
                errors[$"{nameof(statuses)}[{i}].id"] = [$"Status: {id} is not one of the board's columns"];
            else if (!seen.Add(id))
                errors[$"{nameof(statuses)}[{i}].id"] = [$"Status: {id} is listed more than once"];
        }

        if (errors.Count > 0)
            throw new BadRequestException(errors);

        var deletedIds = existingIds
            .Where(x => !seen.Contains(x))
            .ToArray();
        if (deletedIds.Length > 0)
        {
            var deletedAt = dateTimeProvider.UtcNow;

            await context.Issues
                .Where(x => deletedIds.Contains(x.StatusId))
                .ExecuteUpdateAsync(u => u
                    .SetProperty(p => p.DeletedAt, deletedAt)
                    .SetProperty(p => p.DeletedByUserId, deleterId),
                    cancellationToken);

            await context.Statuses
                .Where(x => deletedIds.Contains(x.Id))
                .ExecuteUpdateAsync(u => u
                    .SetProperty(p => p.DeletedAt, deletedAt)
                    .SetProperty(p => p.DeletedByUserId, deleterId),
                    cancellationToken);
        }

        var keptById = await context.Statuses
            .Where(x => seen.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        for (var i = 0; i < statuses.Count; i++)
        {
            var status = statuses[i];
            var sortOrder = i + 1;
            if (status.Id is { } id)
            {
                var kept = keptById[id];
                kept.Name = status.Name;
                kept.Color = status.Color ?? Palette.DefaultStatusColor;
                kept.Category = status.Category;
                kept.SortOrder = sortOrder;
            }
            else
            {
                context.Statuses.Add(new Laraue.Apps.Boards.DataAccess.Models.Status
                {
                    Name = status.Name,
                    EpicId = epicId,
                    SortOrder = sortOrder,
                    Color = status.Color ?? Palette.DefaultStatusColor,
                    Category = status.Category,
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        // Categories may have changed and issues may have gone with a deleted status.
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