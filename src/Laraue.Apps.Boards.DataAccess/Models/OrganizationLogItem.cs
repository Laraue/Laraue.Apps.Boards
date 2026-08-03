using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class OrganizationLogItem
{
    public long Id { get; set; }
    
    public long OrganizationLogId { get; set; }
    public OrganizationLog? OrganizationLog { get; set; }

    public ValueData OldValueData { get; set; } = new ();
    public ValueData NewValueData { get; set; } = new ();
    
    public PropertyType PropertyType { get; set; }
    
    [MaxLength(255)]
    public string? PropertyName { get; set; }
    
    [MaxLength(4096)]
    public string? OldDisplayValue { get; set; }
    
    [MaxLength(4096)]
    public string? NewDisplayValue { get; set; }
}

public record ValueData
{
    /// <summary>
    /// Value identifier (long or GUID identifier serialized as string).
    /// </summary>
    public string? ValueId { get; set; }
}

public enum PropertyType
{
    Attachment,
    Content,
    Attribute,
    Assignee,
    Status,
    Epic,
    Space,
}