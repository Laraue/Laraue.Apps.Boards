namespace Laraue.Apps.Boards.DataAccess.Models;

public class IssueAttributeIntegerValue : IIssueAttributeScalarValue<long>
{
    public long IssueId { get; set; }
    public Issue? Issue { get; set; }

    public long AttributeId { get; set; }
    public Attribute? Attribute { get; set; }

    public required long Value { get; set; }
}
