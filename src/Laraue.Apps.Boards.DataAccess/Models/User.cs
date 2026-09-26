using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class User
{
    public Guid Id { get; set; }
    public Guid GlobalUserId { get; set; }
    /// <summary>
    /// Null for a user who has only ever signed in with Google (see <see cref="GoogleSubject"/>) -
    /// such a user can use the web app, but none of the Telegram bot's features.
    /// </summary>
    public long? TelegramId { get; set; }

    /// <summary>
    /// The Google ID token's <c>sub</c> claim, set when the user signed in with Google. Null for a
    /// Telegram-only user. Google and Telegram sign-ins create separate users for now - linking
    /// them onto one is BRD-218.
    /// </summary>
    [MaxLength(255)]
    public string? GoogleSubject { get; set; }

    /// <summary>
    /// Boards-side presentation name, derived once at sign-up from the sign-in method's profile.
    /// The profile itself (Telegram username/names/language, Google email/name) isn't stored here -
    /// Laraue.Apps.Identity is its source of truth.
    /// </summary>
    // 129 = Telegram's 64-char first/last name limit twice, plus the joining space ("{firstName} {lastName}")
    [MaxLength(129)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(2)]
    public string Initials { get; set; } = string.Empty;

    [MaxLength(7)]
    public string Color { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// UTC timestamp the user was soft-deleted at, or null if the account is active. Set when account
    /// linking moved this user's last sign-in method to another user (their personal organization is
    /// soft-deleted at the same time).
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// The user who soft-deleted this one, if any - for a merge, the user who took over the account.
    /// </summary>
    public Guid? DeletedByUserId { get; set; }
    public User? DeletedByUser { get; set; }
    public IList<Epic>? Epics { get; set; }
    public IList<Space>? Spaces { get; set; }
    public IList<Organization>? Organizations { get; set; }
}