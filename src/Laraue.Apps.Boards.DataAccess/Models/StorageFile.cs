namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// A file kept in Laraue.Apps.Storage (BRD-339): uploads since then, and their previews. Older uploads and files from
/// Telegram messages have a <see cref="TelegramFile"/> instead (BRD-342).
/// </summary>
public class StorageFile
{
    public long Id { get; set; }

    /// <summary>
    /// Link to the files table.
    /// </summary>
    public Guid FileId { get; set; }
    public File? File { get; set; }

    /// <summary>
    /// The file's id in Storage; its public URL is <c>StorageOptions.PublicBaseUrl</c> plus this id. Not unique: Storage returns the same file for the same content uploaded again in an
    /// organization, each upload with a reference of its own.
    /// </summary>
    public Guid ExternalFileId { get; set; }

    /// <summary>
    /// The reference this upload holds on the file in Storage - what Boards confirms once the attachment is saved and
    /// deletes when it's removed (<see cref="StorageReferenceOperation"/>). Null for a preview, which goes with its
    /// original.
    /// </summary>
    public Guid? ReferenceId { get; set; }
}
