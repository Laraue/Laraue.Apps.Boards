using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class Status
{
    public long Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(7)]
    public string Color { get; set; } = string.Empty;
    
    public long EpicId { get; set; }
    public Epic? Epic { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// UTC timestamp the status was soft-deleted at, or null if it is active.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// The user who soft-deleted the status, if any.
    /// </summary>
    public Guid? DeletedByUserId { get; set; }
    public User? DeletedByUser { get; set; }

    public IList<Issue>? Issues { get; set; }
}