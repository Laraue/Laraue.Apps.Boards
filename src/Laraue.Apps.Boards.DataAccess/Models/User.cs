using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// A Boards user: identifiers only. The profile (names, language) lives in Laraue.Apps.Identity, and how
/// the user is shown in an organization is their <see cref="OrganizationUser"/> row there.
/// </summary>
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

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the user last used Boards: an authenticated web request, a Telegram update or an MCP call
    /// (BRD-348). Written at most once an hour per user, so it can be up to an hour behind. Null for a user
    /// never seen since the column was added who has no history either (see the migration's backfill).
    /// Feeds <c>boards_active_users</c> and the retention steps of <c>boards_funnel_users</c>.
    /// </summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>
    /// Where the user came to Boards from (BRD-295): a landing link's <c>ref</c>/<c>utm_source</c>, the referring site,
    /// a bot <c>/start</c> parameter, a Mini App <c>start_param</c>, or <c>invite</c> for an invite link. Set once when
    /// the user is created and never changed. It's the value Laraue.Apps.Identity stored on the user's Boards service
    /// row and returned - Identity normalizes it (lower-case, <c>a-z 0-9 . - _</c>, at most
    /// <see cref="SignUpSourceMaxLength"/>), Boards keeps a copy for its funnel. Null when unknown and for users from
    /// before BRD-295.
    /// </summary>
    [MaxLength(SignUpSourceMaxLength)]
    public string? SignUpSource { get; set; }

    public const int SignUpSourceMaxLength = 64;

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

    /// <summary>
    /// Written into every user and organization token issued for this user; a token carrying an older
    /// value is rejected. Incremented to end all of the user's sessions at once - e.g. when account
    /// linking moves their last sign-in method to another user (BRD-222).
    /// </summary>
    public int TokenVersion { get; set; }
    public IList<Epic>? Epics { get; set; }
    public IList<Space>? Spaces { get; set; }
    public IList<Organization>? Organizations { get; set; }
}