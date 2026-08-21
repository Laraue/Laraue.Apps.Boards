namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// Implemented by issue attribute value entities that store a single comparable
/// <typeparamref name="TValue"/> per (issue, attribute) pair - as opposed to
/// <see cref="IssueAttributeListValue"/>, which resolves to a predefined option instead of
/// holding a value of its own. Lets scalar attribute types (Text, Number, Date) share update logic.
/// </summary>
public interface IIssueAttributeScalarValue<TValue>
{
    long IssueId { get; set; }
    long AttributeId { get; set; }
    TValue Value { get; set; }
}
