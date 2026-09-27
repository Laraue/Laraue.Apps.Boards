using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.DataAccess.Enums;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class OrganizationUser
{
    public long Id { get; set; }
    
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public AdminAccessLevel AdminAccessLevel { get; set; }

    /// <summary>
    /// The member's name in this organization, set by the member themselves. Null means the default
    /// <see cref="Models.User.DisplayName"/> is shown - read the effective name as
    /// <c>DisplayName ?? User.DisplayName</c>.
    /// </summary>
    [MaxLength(129)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Initials derived from <see cref="DisplayName"/>, set and cleared together with it.
    /// </summary>
    [MaxLength(2)]
    public string? Initials { get; set; }

    /// <summary>
    /// The member's color in this organization, set by the member themselves. Null means the default
    /// <see cref="Models.User.Color"/> is shown. Independent of <see cref="DisplayName"/>.
    /// </summary>
    [MaxLength(7)]
    public string? Color { get; set; }

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
