using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Laraue.Apps.Boards.Services;

public interface ICoreSpacesService
{
    Task<string> Create(
        long organizationId,
        Guid creatorId,
        string key,
        string name,
        string color,
        CancellationToken cancellationToken);
    
    Task Update(
        long id,
        Action<UpdateSettersBuilder<Space>> setters,
        CancellationToken cancellationToken);
    
    Task Delete(
        long id,
        Guid deleterId,
        CancellationToken cancellationToken);

    Task<long> GetSpaceIdBySpaceKey(
        long organizationId,
        string spaceKey,
        CancellationToken cancellationToken);
}

public class CoreSpacesService(
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider)
    : ICoreSpacesService
{
    public async Task<string> Create(
        long organizationId,
        Guid creatorId,
        string key,
        string name,
        string color,
        CancellationToken cancellationToken)
    {
        var dateTime = dateTimeProvider.UtcNow;
        
        var entity = new Space
        {
            CreatorId = creatorId,
            Name = name,
            Color = color,
            CreatedAt = dateTime,
            UpdatedAt = dateTime,
            Key = key.ToUpper(),
            OrganizationId = organizationId,
            Epics = new List<Epic>
            {
                OrganizationDefaults.GetNewBacklogEpicEntity(creatorId, dateTime)
            }
        };
        
        context.Spaces.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        
        return entity.Key;
    }

    public Task Update(long id, Action<UpdateSettersBuilder<Space>> setters, CancellationToken cancellationToken)
    {
        var date = dateTimeProvider.UtcNow;
        
        return context.ActiveSpaces()
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(
                update =>
                {
                    setters(update);
                    update
                        .SetProperty(p => p.UpdatedAt, date);
                },
                cancellationToken);
    }

    public async Task Delete(long id, Guid deleterId, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var deletedAt = dateTimeProvider.UtcNow;

        var epicIds = context.Epics
            .Where(x => x.SpaceId == id)
            .Select(x => (long?)x.Id);

        var statusIds = context.Statuses
            .Where(x => epicIds.Contains(x.EpicId))
            .Select(x => (long?)x.Id);

        await context.Issues
            .Where(x => statusIds.Contains(x.StatusId))
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.DeletedAt, deletedAt)
                .SetProperty(p => p.DeletedByUserId, deleterId),
                cancellationToken);

        await context.Statuses
            .Where(x => epicIds.Contains(x.EpicId))
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.DeletedAt, deletedAt)
                .SetProperty(p => p.DeletedByUserId, deleterId),
                cancellationToken);

        await context.Epics
            .Where(x => x.SpaceId == id)
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.DeletedAt, deletedAt)
                .SetProperty(p => p.DeletedByUserId, deleterId),
                cancellationToken);

        await context.Spaces
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.DeletedAt, deletedAt)
                .SetProperty(p => p.DeletedByUserId, deleterId),
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public Task<long> GetSpaceIdBySpaceKey(long organizationId, string spaceKey, CancellationToken cancellationToken)
    {
        return context.ActiveSpaces()
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => x.Key == spaceKey)
            .Select(x => x.Id)
            .FirstOrThrowNotFoundEFAsync($"Space: {spaceKey} is not found", cancellationToken);
    }
}