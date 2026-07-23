using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// All files that was received or uploaded by app Telegram Bot.
/// </summary>
public class TelegramFile
{
    public long Id { get; set; }

    /// <summary>
    /// Link to the files table.
    /// </summary>
    public Guid FileId { get; set; }
    public File? File { get; set; }

    /// <summary>
    /// Telegram file identifier to request file bytes.
    /// </summary>
    [MaxLength(255)]
    public required string ExternalFileId { get; set; }
    
    /// <summary>
    /// Telegram unique file identifier.
    /// Two equal files will have different <see cref="FileId"/>, but the same unique identifier.
    /// </summary>
    [MaxLength(64)]
    public required string ExternalFileUniqueId { get; set; }
}