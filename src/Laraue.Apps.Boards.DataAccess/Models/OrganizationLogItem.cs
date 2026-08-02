using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class OrganizationLogItem
{
    public long Id { get; set; }
    
    public long OrganizationLogId { get; set; }
    public OrganizationLog? OrganizationLog { get; set; }

    public ValueData OldValueData { get; set; } = new ();
    public ValueData NewValueData { get; set; } = new ();
    
    public ChangeAction Action { get; set; }
    public IssueUpdateEntityType EntityType { get; set; }
    
    [MaxLength(4096)]
    public string? OldDisplayValue { get; set; }
    
    [MaxLength(4096)]
    public string? NewDisplayValue { get; set; }

    [MaxLength(255)]
    public string? PropertyName { get; set; }
}

public record ValueData
{
    /// <summary>
    /// Value identifier (long or GUID identifier serialized as string).
    /// </summary>
    public string? ValueId { get; set; }
    
    /// <summary>
    /// When the change related to the issue entity the field is filled.
    /// For example, comment attachment update will store comment id here.
    /// </summary>
    public string? ParentValueId { get; set; }

    /// <summary>
    /// Used when needs to store information about value color.
    /// </summary>
    public string? Color { get; set; }
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
    Status,
}

public enum ChangeAction
{
    Create,
    Update,
    Delete,
}