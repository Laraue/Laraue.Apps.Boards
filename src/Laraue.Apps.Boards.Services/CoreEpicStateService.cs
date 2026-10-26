using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Keeps the stored state of epics that follows from their issues: when they were last touched and their
/// <see cref="EpicStatus"/>.
/// </summary>
public interface ICoreEpicStateService
{
    /// <summary>
    /// Moves <see cref="DataAccess.Models.Epic.TouchedAt"/> of the given epics forward to <paramref name="touchedAt"/>.
    /// </summary>
    Task Touch(
        long[] epicIds,
        DateTime touchedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sets <see cref="DataAccess.Models.Epic.Status"/> of the given epics from their active issues:
    /// <see cref="EpicStatus.New"/> with no issues or all of them Created, <see cref="EpicStatus.Done"/> with all
    /// of them Completed, <see cref="EpicStatus.Active"/> otherwise. Call it in the transaction of every change to
    /// an issue's status, deletion or a status's category, so the stored value never goes stale. Requires a
    /// transaction: the epics stay locked until it ends, so concurrent changes recalculate one after another.
    /// </summary>
    Task RecalculateStatuses(
        long[] epicIds,
        CancellationToken cancellationToken);
}

public class CoreEpicStateService(DatabaseContext context) : ICoreEpicStateService
{
    public Task Touch(
        long[] epicIds,
        DateTime touchedAt,
        CancellationToken cancellationToken)
    {
        return context.Epics
            .Where(x => ((IEnumerable<long>)epicIds).Contains(x.Id))
            .ExecuteUpdateAsync(x => x
                .SetProperty(
                    p => p.TouchedAt,
                    old => old!.TouchedAt > touchedAt ? old.TouchedAt : touchedAt),
                cancellationToken);
    }

    public async Task RecalculateStatuses(
        long[] epicIds,
        CancellationToken cancellationToken)
    {
        if (epicIds.Length == 0)
            return;

        await context.LockEpics(epicIds, cancellationToken);

        var epics = await context.Epics
            .Where(x => ((IEnumerable<long>)epicIds).Contains(x.Id))
            .Select(e => new
            {
                e.Id,
                e.Status,
                NewStatus = !context.Issues
                    .Any(i => i.DeletedAt == null
                        && i.Status!.EpicId == e.Id
                        && i.Status.Category != StatusCategory.Created)
                    ? EpicStatus.New
                    : !context.Issues
                        .Any(i => i.DeletedAt == null
                            && i.Status!.EpicId == e.Id
                            && i.Status.Category != StatusCategory.Completed)
                        ? EpicStatus.Done
                        : EpicStatus.Active,
            })
            .ToListAsync(cancellationToken);

        var changed = epics
            .Where(x => x.Status != x.NewStatus)
            .ToArray();

        if (changed.Length == 0)
            return;

        var changedIds = changed
            .Select(x => x.Id)
            .ToArray();

        var newIds = changed
            .Where(x => x.NewStatus == EpicStatus.New)
            .Select(x => x.Id)
            .ToArray();

        var doneIds = changed
            .Where(x => x.NewStatus == EpicStatus.Done)
            .Select(x => x.Id)
            .ToArray();

        await context.Epics
            .Where(x => ((IEnumerable<long>)changedIds).Contains(x.Id))
            .ExecuteUpdateAsync(
                u => u.SetProperty(
                    e => e.Status,
                    e => ((IEnumerable<long>)newIds).Contains(e.Id)
                        ? EpicStatus.New
                        : ((IEnumerable<long>)doneIds).Contains(e.Id)
                            ? EpicStatus.Done
                            : EpicStatus.Active),
                cancellationToken);
    }
}
