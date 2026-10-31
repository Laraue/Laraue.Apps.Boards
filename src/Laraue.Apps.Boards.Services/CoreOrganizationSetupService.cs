using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services.Metrics;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

public interface ICoreOrganizationSetupService
{
    /// <summary>
    /// Marks the organization as set up (BRD-293). The setup window saves the space name and the demo data through
    /// the regular endpoints first; <paramref name="demoDataAdded"/> only labels the metric. False, with nothing
    /// changed, when it was already set up.
    /// </summary>
    Task<bool> Complete(
        long organizationId,
        bool demoDataAdded,
        CancellationToken cancellationToken);
}

public class CoreOrganizationSetupService(
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider,
    BoardsMetrics metrics)
    : ICoreOrganizationSetupService
{
    public async Task<bool> Complete(
        long organizationId,
        bool demoDataAdded,
        CancellationToken cancellationToken)
    {
        // Conditional, so of two concurrent calls only one sees a row updated.
        var updatedCount = await context.ActiveOrganizations()
            .Where(x => x.Id == organizationId && x.SetupCompletedAt == null)
            .ExecuteUpdateAsync(
                u => u.SetProperty(x => x.SetupCompletedAt, dateTimeProvider.UtcNow),
                cancellationToken);

        if (updatedCount == 0)
            return false;

        metrics.RecordOrganizationSetupCompleted(demoDataAdded);

        return true;
    }
}
