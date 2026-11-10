using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// A message the bot sent a user on its own, not as a reply (BRD-354): one row per user and
/// <see cref="Kind"/>, so each kind goes to a user at most once. Kept when sending failed - a user who
/// signed in on the web without allowing the bot to write can't be reached, and that is worth counting.
/// </summary>
public class UserBotMessage
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public BotMessageKind Kind { get; set; }

    /// <summary>
    /// When sending was attempted.
    /// </summary>
    public DateTime SentAt { get; set; }

    public BotMessageStatus Status { get; set; }

    /// <summary>
    /// Telegram's error when <see cref="Status"/> is <see cref="BotMessageStatus.Failed"/>, cut to
    /// <see cref="ErrorMaxLength"/>.
    /// </summary>
    [MaxLength(ErrorMaxLength)]
    public string? Error { get; set; }

    public const int ErrorMaxLength = 500;
}

public enum BotMessageKind
{
    /// <summary>
    /// Sent right after a user signed up with Telegram on the web or in the Mini App - those who sign
    /// up by writing to the bot get the <c>/start</c> reply instead.
    /// </summary>
    Welcome = 1,
}

public enum BotMessageStatus
{
    Sent = 0,
    Failed = 1,
}
