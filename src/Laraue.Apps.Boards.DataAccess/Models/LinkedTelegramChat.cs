using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// A Telegram group/supergroup chat linked to an organization (and optionally a
/// space/epic/status) so that messages in it can be turned into cards.
/// </summary>
public class LinkedTelegramChat
{
    public long Id { get; set; }

    /// <summary>
    /// Telegram chat identifier.
    /// </summary>
    public required long ExternalChatId { get; init; }

    /// <summary>
    /// Best-effort display name of the chat.
    /// </summary>
    [MaxLength(256)]
    public string? Title { get; set; }

    /// <summary>
    /// Status of entities that will be created through the chat. 
    /// </summary>
    public long StatusId { get; set; }
    public Status? Status { get; set; }

    /// <summary>
    /// Who creates the link.
    /// </summary>
    public Guid? OwnerId { get; set; }
    public User? Owner { get; set; }

    /// <summary>
    /// When the link was created.
    /// </summary>
    public DateTime? LinkedAt { get; set; }
    
    /// <summary>
    /// When the link was removed.
    /// </summary>
    public DateTime? UnlinkedAt { get; set; }
    
    /// <summary>
    /// Messages save mode.
    /// </summary>
    public SaveMode SaveMode { get; set; }
}

/// <summary>
/// How the bot will react to chat messages.
/// </summary>
public enum SaveMode
{
    /// <summary>
    /// Bot will save each message as new card.
    /// Nice for personal chats where each message should become a card.
    /// </summary>
    EachMessage,
    
    /// <summary>
    /// Bot will save only messages that was replied to the bot with command /save. 
    /// </summary>
    BotMentionedMessages,
}