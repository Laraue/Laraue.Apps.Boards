using System.ComponentModel.DataAnnotations;

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
    /// The message's own text/caption as sent. Stored regardless of save mode so that /save can
    /// build a card's content later - the Bot API has no way to fetch an arbitrary past
    /// message's content, so this is the only place it can come from by then.
    /// </summary>
    [MaxLength(4096)]
    public string? Text { get; set; }

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