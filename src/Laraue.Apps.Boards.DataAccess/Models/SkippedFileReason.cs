namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// Why a file sent to the Telegram bot wasn't saved as an attachment.
/// </summary>
public enum SkippedFileReason
{
    /// <summary>
    /// The content isn't one of the attachment file types.
    /// </summary>
    UnsupportedType,

    /// <summary>
    /// The file is larger than its type's limit.
    /// </summary>
    TooLarge,
}
