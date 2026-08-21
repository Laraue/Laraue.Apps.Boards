namespace Laraue.Apps.Boards.DataAccess.Models;

public class IssueAttributeDateTimeValue : IIssueAttributeScalarValue<DateTime>
{
    public long IssueId { get; set; }
    public Issue? Issue { get; set; }

    public long AttributeId { get; set; }
    public Attribute? Attribute { get; set; }

    public required DateTime Value { get; set; }
}
