namespace Laraue.Apps.Boards.Services.AttributeRequests;

public record StringAttributeValue : AttributeValue
{
    public required string Value { get; set; }
}
