using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Npgsql;

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

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsSpaceKeyViolation(ex))
        {
            throw new BadRequestException(nameof(key), SpaceKeyAlreadyExistsMessage);
        }
        
        return entity.Key;
    }

    public async Task Update(long id, Action<UpdateSettersBuilder<Space>> setters, CancellationToken cancellationToken)
    {
        var date = dateTimeProvider.UtcNow;

        try
        {
            await context.ActiveSpaces()
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
        catch (PostgresException ex) when (IsSpaceKeyViolation(ex))
        {
            throw new BadRequestException("key", SpaceKeyAlreadyExistsMessage);
        }
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

    private const string SpaceKeyAlreadyExistsMessage = "A space with this key already exists";

    // The unique index is the only race-free check, so a duplicate key is detected by the violation itself.
    private static bool IsSpaceKeyViolation(Exception ex)
    {
        var pgException = ex as PostgresException ?? ex.InnerException as PostgresException;

        return pgException is
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: Constraints.SpaceKeyIndexName,
        };
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