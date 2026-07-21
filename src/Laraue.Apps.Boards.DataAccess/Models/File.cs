using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// Represents one file in the system. The table doesn't contain information about how it is stored.
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
    [MaxLength(32)]
    public required string? MimeType { get; set; }
    
    /// <summary>
    /// Link exists when the file is stored in Telegram (current version stores in Telegram only, later will use different places.)
    /// </summary>
    public TelegramFile? TelegramFile { get; set; }
}