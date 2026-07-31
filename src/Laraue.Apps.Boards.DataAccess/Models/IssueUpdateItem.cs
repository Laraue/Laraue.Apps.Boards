using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class IssueUpdateItem
{
    public long Id { get; set; }
    
    public long IssueUpdateId { get; set; }
    public IssueUpdate? IssueUpdate { get; set; }
    
    /// <summary>
    /// Value identifier (long or GUID identifier serialized as string).
    /// </summary>
    [MaxLength(36)]
    public string? OldValueId { get; set; }
    
    /// <summary>
    /// Value identifier (long or GUID identifier serialized as string).
    /// </summary>
    [MaxLength(36)]
    public string? NewValueId { get; set; }
    
    /// <summary>
    /// When the change related to the issue entity the field is filled.
    /// For example, comment attachment update will store comment id here.
    /// </summary>
    [MaxLength(36)]
    public string? ParentValueId { get; set; }
    
    public ChangeAction Action { get; set; }
    public IssueUpdateEntityType EntityType { get; set; }
    
    [MaxLength(4096)]
    public string? OldDisplayValue { get; set; }
    
    [MaxLength(4096)]
    public string? NewDisplayValue { get; set; }

    [MaxLength(255)]
    public string? PropertyName { get; set; }
}

public enum IssueUpdateEntityType
{
    Attachment,
    Content,
    CommentContent,
    CommentAttachment,
    Property,
    Issue,
    Assignee,
}

public enum ChangeAction
{
    Create,
    Update,
    Delete,
}