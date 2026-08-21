namespace Laraue.Apps.Boards.DataAccess.Models;

public class IssueAttributeDateValue : IIssueAttributeScalarValue<DateOnly>
{
    public long IssueId { get; set; }
    public Issue? Issue { get; set; }

    public long AttributeId { get; set; }
    public Attribute? Attribute { get; set; }

    public required DateOnly Value { get; set; }
}
