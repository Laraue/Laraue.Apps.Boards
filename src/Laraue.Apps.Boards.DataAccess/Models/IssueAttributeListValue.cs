namespace Laraue.Apps.Boards.DataAccess.Models;

public class IssueAttributeListValue : IIssueAttributeValue
{
    public long Id { get; set; }
    
    public long IssueId { get; set; }
    public Issue? Issue { get; set; }
    
    public long AttributeId { get; set; }
    public Attribute? Attribute { get; set; }
    
    public long AttributeListValueId { get; set; }
    public AttributeListValue? AttributeListValue { get; set; }
}