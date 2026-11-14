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
    /// The chat's public @username (without "@"), null for a private chat. Set on link and refreshed from
    /// incoming messages; a public supergroup's message links use it (t.me/{username}/{messageId}).
    /// </summary>
    [MaxLength(32)]
    public string? Username { get; set; }

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

    /// <summary>
    /// How the bot confirms that a message became a card, both on auto-save and on /save. Chats linked before
    /// BRD-361 got the mode matching what they did then (<see cref="SaveMode.EachMessage"/> reacted, /save linked);
    /// the personal chat linked at sign-up starts with <see cref="ReplyMode.IssueLink"/>.
    /// </summary>
    public ReplyMode ReplyMode { get; set; }
}

/// <summary>
/// How the bot replies to a message it saved.
/// </summary>
public enum ReplyMode
{
    /// <summary>
    /// A reaction on the message: 👍 when a card is created, ❤ when an edit (or a repeated /save) updates it.
    /// </summary>
    Reaction,

    /// <summary>
    /// A reply to the message with the new card's link, so the user sees where it was saved. Edits still get ❤.
    /// </summary>
    IssueLink,
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