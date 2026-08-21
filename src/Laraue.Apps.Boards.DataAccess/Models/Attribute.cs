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