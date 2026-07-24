using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class Issue
{
    public long Id { get; set; }
    
    /// <summary>
    /// Message content.
    /// </summary>
    [MaxLength(4096)]
    public required string? Content { get; set; }
    
    /// <summary>
    /// Issue creation date.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Issue update date.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
    
    /// <summary>
    /// The user which created the issue.
    /// </summary>
    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }
    
    /// <summary>
    /// The user issue was assigned to
    /// </summary>
    public Guid AssigneeId { get; set; }
    public User? Assignee { get; set; }

    /// <summary>
    /// Actual message status.
    /// </summary>
    public long StatusId { get; set; }
    public Status? Status { get; set; }
    
    public TelegramMessage? TelegramMessage { get; set; }
    public long? TelegramMessageId { get; set; }
    
    public IssueNumber? IssueNumber { get; set; }
    public List<IssueAttributeTextValue>? TextAttributes { get; set; }
    public List<IssueAttributeListValue>? ListAttributes { get; set; }
    public List<IssueAttachment>? IssueAttachments { get; set; }
    public List<IssueComment>? IssueComments { get; set; }
}