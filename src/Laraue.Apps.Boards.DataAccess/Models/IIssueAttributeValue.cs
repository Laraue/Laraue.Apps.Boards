namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// An issue's value of one attribute, whatever its type: one of the six issue attribute value tables
/// (<see cref="IssueAttributeTextValue"/>, <see cref="IssueAttributeListValue"/>, ...). Lets code that only needs
/// which issue has a value for which attribute treat them alike - see
/// <see cref="DatabaseContextIssueAttributeValueExtensions"/>.
/// </summary>
public interface IIssueAttributeValue
{
    long IssueId { get; set; }
    long AttributeId { get; set; }
}
