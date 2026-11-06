using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// Represents one file in the system. Where it is stored is in <see cref="TelegramFile"/> or <see cref="StorageFile"/>.
/// </summary>
public class File
{
    /// <summary>
    /// System file identifier.
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// The file size in bytes.
    /// </summary>
    public long? Size { get; set; }
    
    /// <summary>
    /// File name, e.g. 'dog.jpg'. The human-readable attachment name.
    /// </summary>
    [MaxLength(255)]
    public string? Name { get; set; }
    
    /// <summary>
    /// The file mime type, e.g. 'image/jpeg'.
    /// </summary>
    [MaxLength(128)]
    public required string? MimeType { get; set; }
    
    /// <summary>
    /// Link exists when the file is stored in Telegram: uploads from before BRD-339 (kept in the Telegram files chat,
    /// BRD-342) and files from Telegram messages.
    /// </summary>
    public TelegramFile? TelegramFile { get; set; }

    /// <summary>
    /// Link exists when the file is kept in Laraue.Apps.Storage: uploads since BRD-339 and their previews.
    /// </summary>
    public StorageFile? StorageFile { get; set; }
}