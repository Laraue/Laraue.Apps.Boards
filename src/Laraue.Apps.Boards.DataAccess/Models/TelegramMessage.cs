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
    /// Attachment id (exists for images / videos / files).
    /// </summary>
    public Guid? AttachmentId { get; set; }
    public Attachment? Attachment { get; set; }

    /// <summary>
    /// The chat link this message was saved through.
    /// </summary>
    public long? LinkedTelegramChatId { get; set; }
    public LinkedTelegramChat? LinkedTelegramChat { get; set; }
    
    /// <summary>
    /// The card related to this message.
    /// </summary>
    public Issue? Issue { get; set; }
}