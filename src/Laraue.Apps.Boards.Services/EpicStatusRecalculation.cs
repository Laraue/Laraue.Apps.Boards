using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

public static class EpicStatusRecalculation
{
    /// <summary>
    /// Sets <see cref="DataAccess.Models.Epic.Status"/> of the given epics from their active issues, in one UPDATE:
    /// <see cref="EpicStatus.New"/> with no issues or all of them Created, <see cref="EpicStatus.Done"/> with all
    /// of them Completed, <see cref="EpicStatus.Active"/> otherwise. Call it in the transaction of every change to
    /// an issue's status, deletion or a status's category, so the stored value never goes stale.
    /// </summary>
    public static Task<int> RecalculateEpicStatuses(
        this DatabaseContext context,
        long[] epicIds,
        CancellationToken cancellationToken)
    {
        if (epicIds.Length == 0)
            return Task.FromResult(0);

        return context.Epics
            .Where(x => ((IEnumerable<long>)epicIds).Contains(x.Id))
            .ExecuteUpdateAsync(
                u => u.SetProperty(
                    e => e.Status,
                    e => !context.Issues
                        .Any(i => i.DeletedAt == null
                            && i.Status!.EpicId == e.Id
                            && i.Status.Category != StatusCategory.Created)
                        ? EpicStatus.New
                        : !context.Issues
                            .Any(i => i.DeletedAt == null
                                && i.Status!.EpicId == e.Id
                                && i.Status.Category != StatusCategory.Completed)
                            ? EpicStatus.Done
                            : EpicStatus.Active),
                cancellationToken);
    }
}
