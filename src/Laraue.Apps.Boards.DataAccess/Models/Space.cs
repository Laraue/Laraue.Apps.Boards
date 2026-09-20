using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// Space is the direct alternative of Jira Space (ex Project).
/// </summary>
public class Space
{
    public long Id { get; set; }
    
    [MaxLength(128)]
    public required string Name { get; set; }
    
    [MaxLength(7)]
    public required string Color { get; set; }
    
    [MaxLength(3)]
    public required string Key { get; set; }
    
    public Guid CreatorId { get; set; }
    public User? Creator { get; set; }
    
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    
    /// <summary>
    /// Epic creation date.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Epic attribute update date.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// UTC timestamp the space was soft-deleted at, or null if it is active.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// The user who soft-deleted the space, if any.
    /// </summary>
    public Guid? DeletedByUserId { get; set; }
    public User? DeletedByUser { get; set; }

    public bool IsDefault { get; set; }
    
    /// <summary>
    /// Epics linked to the space.
    /// </summary>
    public IList<Epic>? Epics { get; set; }
    public IList<DirectSpacePermission>? Users { get; set; }
}