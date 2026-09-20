using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class IssueComment
{
    public long Id { get; set; }
    
    [MaxLength(Constraints.MaxCommentLength)]
    public required string Text { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// UTC timestamp the comment was soft-deleted at, or null if it is active.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// The user who soft-deleted the comment, if any.
    /// </summary>
    public Guid? DeletedByUserId { get; set; }
    public User? DeletedByUser { get; set; }

    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }
    
    public long IssueId { get; set; }
    public Issue? Issue { get; set; }

    public List<IssueCommentAttachment> Attachments { get; set; } = [];
}