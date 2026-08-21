using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.DataAccess.Enums;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class Epic
{
    public long Id { get; set; }
    
    [MaxLength(128)]
    public required string Name { get; set; }
    
    [MaxLength(7)]
    public required string Color { get; set; }
    
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>
    /// The space the epic belongs to.
    /// </summary>
    public long SpaceId { get; set; }
    public Space? Space { get; set; }

    /// <summary>
    /// Epic creation date.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Epic attribute update date.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
    
    /// <summary>
    /// When the epic or issues in epic were updated.
    /// Property to sort epics.
    /// </summary>
    public DateTime TouchedAt { get; set; }
    
    public bool IsDefault { get; set; }

    public EpicStatus Status { get; set; }

    public IList<Status>? Statuses { get; set; }
}