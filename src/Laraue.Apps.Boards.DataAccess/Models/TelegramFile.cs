using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// All files that was received or uploaded by app Telegram Bot.
/// </summary>
public class TelegramFile
{
    public Guid Id { get; set; }

    [MaxLength(255)]
    public required string FileId { get; set; }
    
    [MaxLength(64)]
    public required string FileUniqueId { get; set; }
    
    public long? Size { get; set; }

    [MaxLength(255)]
    public string? Name { get; set; }
    
    [MaxLength(32)]
    public required string? MimeType { get; set; }
}