using System.Text.Json.Serialization;
using Laraue.Apps.Boards.DataAccess.Enums;

namespace Laraue.Apps.Boards.Services.Sorting;

[JsonDerivedType(typeof(ByAttributeIssueSorting), "attribute")]
[JsonDerivedType(typeof(ByPropertyIssueSorting), "property")]
public abstract record IssueSorting
{
    public SortingDirection Direction { get; set; }
}

/// <summary>
/// Sorting applied by attribute id.
/// </summary>
public record ByAttributeIssueSorting : IssueSorting
{
    public long AttributeId { get; set; }
}

/// <summary>
/// Sorting applied by issue property.
/// </summary>
public record ByPropertyIssueSorting : IssueSorting
{
    public IssueProperty Property { get; set; }
}