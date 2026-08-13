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
    /// Attachment id (exists for images / videos / files).
    /// </summary>
    public Guid? AttachmentId { get; set; }
    public Attachment? Attachment { get; set; }
    
    /// <summary>
    /// The card related to this message.
    /// </summary>
    public Issue? Issue { get; set; }

    /// <summary>
    /// Caption/text captured while this message was passively recorded as part of a
    /// linked group chat's media group, before any card existed for the group. Only
    /// meaningful while <see cref="Issue"/> is null - once a card is created for the
    /// group, its content lives on the <see cref="DataAccess.Models.Issue.Content"/> instead.
    /// </summary>
    [MaxLength(4096)]
    public string? PendingContent { get; set; }
}