using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.Storage;

/// <summary>
/// Queues calls to Laraue.Apps.Storage that must follow Boards' own commit (<see cref="StorageReferenceOperation"/>):
/// rows saved in the caller's transaction and sent afterwards by <c>StorageReferenceOperationsSender</c>, so a
/// confirm or delete is never lost to a failure between the two systems. Database only - any host can queue.
/// </summary>
public interface IStorageReferenceOutbox
{
    /// <summary>
    /// Queues confirming the uploads' references, once the attachments using them are saved. Storage deletes an upload
    /// left unconfirmed for a day.
    /// </summary>
    Task QueueConfirm(IEnumerable<MediaInfo> uploads, CancellationToken cancellationToken);

    /// <summary>
    /// Queues deleting the Storage references of these attachments' files - call it before the attachments are
    /// deleted. Attachments whose files aren't in Storage (Telegram) are skipped.
    /// </summary>
    Task QueueDelete(IQueryable<Attachment> attachments, CancellationToken cancellationToken);
}

public class StorageReferenceOutbox(DatabaseContext context, IDateTimeProvider dateTimeProvider) : IStorageReferenceOutbox
{
    public Task QueueConfirm(IEnumerable<MediaInfo> uploads, CancellationToken cancellationToken)
    {
        var referenceIds = uploads
            .Select(x => x.StorageReferenceId)
            .OfType<Guid>()
            .ToArray();

        return Queue(referenceIds, StorageReferenceOperationKind.Confirm, cancellationToken);
    }

    public async Task QueueDelete(IQueryable<Attachment> attachments, CancellationToken cancellationToken)
    {
        var referenceIds = await attachments
            .Select(x => x.File!.StorageFile!.ReferenceId)
            .Where(x => x != null)
            .Select(x => x!.Value)
            .ToArrayAsync(cancellationToken);

        await Queue(referenceIds, StorageReferenceOperationKind.Delete, cancellationToken);
    }

    private async Task Queue(
        IReadOnlyCollection<Guid> referenceIds,
        StorageReferenceOperationKind kind,
        CancellationToken cancellationToken)
    {
        if (referenceIds.Count == 0)
            return;

        var now = dateTimeProvider.UtcNow;
        context.StorageReferenceOperations.AddRange(referenceIds.Select(referenceId => new StorageReferenceOperation
        {
            ReferenceId = referenceId,
            Kind = kind,
            CreatedAt = now,
            NextAttemptAt = now,
        }));

        await context.SaveChangesAsync(cancellationToken);
    }
}
