using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// One of the user defined attributes.
/// </summary>
public class Attribute
{
    public long Id { get; set; }
    
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    
    [MaxLength(255)]
    public required string Name { get; set; }
    
    [MaxLength(7)]
    public required string Color { get; set; }
    
    public required AttributeType AttributeType { get; set; }

    /// <summary>
    /// Whether the attribute is used in spaces and epics that don't override the attribute settings (BRD-179).
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Whether the attribute's value is shown on issue cards in spaces and epics that don't override the attribute
    /// settings.
    /// </summary>
    public bool ShowOnCard { get; set; }

    /// <summary>
    /// Position of the attribute among the organization's attributes - in forms and on cards.
    /// </summary>
    public int SortOrder { get; set; }
    
    public List<AttributeListValue>? AttributeListValues { get; set; }
}

public enum AttributeType
{
    Text,
    List,
    Integer,
    Decimal,
    Date,
    DateTime,
}