using System.Text.Json.Serialization;

namespace Laraue.Apps.Boards.Services.AttributeRequests;

/// <summary>
/// A caller-supplied value to set on an issue attribute, already resolved to the attribute's id
/// and already typed as the attribute's own <see cref="DataAccess.Models.AttributeType"/> -
/// callers (the REST API's request body, or the MCP host after parsing a caller's plain text)
/// build one of these per attribute before handing them to
/// <see cref="ICoreIssueAttributesService.BuildSetRequests"/>.
/// </summary>
[JsonDerivedType(typeof(EnumAttributeValue), "enum")]
[JsonDerivedType(typeof(StringAttributeValue), "string")]
[JsonDerivedType(typeof(IntegerAttributeValue), "integer")]
[JsonDerivedType(typeof(DecimalAttributeValue), "decimal")]
[JsonDerivedType(typeof(DateAttributeValue), "date")]
[JsonDerivedType(typeof(DateTimeAttributeValue), "datetime")]
public abstract record AttributeValue
{
    public required long AttributeId { get; set; }
}
