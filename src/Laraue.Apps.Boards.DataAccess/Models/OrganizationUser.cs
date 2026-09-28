using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.DataAccess.Enums;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// A user's membership in an organization - also how they're shown there. Kept after the user leaves
/// or is removed (<see cref="LeftAt"/>), so issues, comments and history still show their name.
/// </summary>
public class OrganizationUser
{
    public long Id { get; set; }
    
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public AdminAccessLevel AdminAccessLevel { get; set; }

    /// <summary>
    /// How the member is shown in this organization. Copied from the user's Laraue.Apps.Identity
    /// profile when they join (Identity is the source of truth for the profile) and kept from then
    /// on - a later change of the global profile doesn't touch it; the member can change it here.
    /// </summary>
    // 257 = Identity's display name limit: given + " " + family name, 128 each.
    [MaxLength(257)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Initials matching <see cref="DisplayName"/>, set together with it.
    /// </summary>
    [MaxLength(2)]
    public string Initials { get; set; } = string.Empty;

    /// <summary>
    /// The member's color in this organization - a random palette color on joining, changeable by the
    /// member.
    /// </summary>
    [MaxLength(7)]
    public string Color { get; set; } = string.Empty;

    /// <summary>
    /// When the user left or was removed from the organization; null for a current member. A former
    /// member has no permissions (they're cleared on leaving) and isn't a member for any check - read
    /// members through <c>ActiveOrganizationUsers()</c>. Joining again brings the row back, with the
    /// name/color it had.
    /// </summary>
    public DateTime? LeftAt { get; set; }

    public bool CanRead { get; set; }
    public bool CanManageRetros { get; set; }
    public bool CanCreateSpaces { get; set; }
    public bool CanUpdateSpaces { get; set; }
    public bool CanDeleteSpaces { get; set; }
    public bool CanCreateEpics { get; set; }
    public bool CanUpdateEpics { get; set; }
    public bool CanDeleteEpics { get; set; }
    public bool CanCreateIssues { get; set; }
    public bool CanUpdateIssues { get; set; }
    public bool CanDeleteIssues { get; set; }
}
