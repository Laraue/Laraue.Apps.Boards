namespace Laraue.Apps.Boards.DataAccess.Models;

public class TelegramMessage
{
    public long Id { get; set; }

    /// <summary>
    /// Telegram message identifier.
    /// </summary>
    public required int ExternalMessageId { get; init; }
    public required long ExternalChatId { get; init; }
    
    public long? TelegramMediaGroupId { get; init; }
    public TelegramMediaGroup? TelegramMediaGroup { get; set; }
    
    /// <summary>
    /// File id (exists for images / videos / files).
    /// </summary>
    public long? TelegramFileId { get; set; }
    public TelegramFile? TelegramFile { get; set; }
    
    /// <summary>
    /// Preview file id (exists for images and videos).
    /// </summary>
    public long? TelegramPreviewFileId { get; set; }
    public TelegramFile? TelegramPreviewFile { get; set; }
    
    /// <summary>
    /// The card related to this message.
    /// </summary>
    public Issue? Issue { get; set; }
    
    /// <summary>
    /// Attachment type when message contain attachment.
    /// </summary>
    public AttachmentType? AttachmentType { get; set; }
}