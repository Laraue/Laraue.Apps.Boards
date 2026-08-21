namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// Stores a whole-number attribute value as <c>bigint</c> (C# <see cref="long"/>), matching every
/// other numeric column in this schema, rather than being capped at <see cref="int"/>'s range.
/// </summary>
public class IssueAttributeIntegerValue : IIssueAttributeScalarValue<long>
{
    public long IssueId { get; set; }
    public Issue? Issue { get; set; }

    public long AttributeId { get; set; }
    public Attribute? Attribute { get; set; }

    public required long Value { get; set; }
}
