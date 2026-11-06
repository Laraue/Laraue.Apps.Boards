using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.Services;

public record MediaInfo
{
    public required Guid? PreviewFileId { get; set; }
    public required Guid OriginalFileId { get; set; }
    public required AttachmentType Type { get; set; }
    public required string? FileName { get; set; }

    /// <summary>
    /// For an upload kept in Laraue.Apps.Storage, its pending reference, confirmed through the outbox
    /// (<c>IStorageReferenceOutbox.QueueConfirm</c>) once the attachment is saved. Null for files from Telegram.
    /// </summary>
    public Guid? StorageReferenceId { get; init; }
}