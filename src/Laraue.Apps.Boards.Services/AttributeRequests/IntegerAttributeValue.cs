namespace Laraue.Apps.Boards.Services.AttributeRequests;

public record IntegerAttributeValue : AttributeValue
{
    public required long Value { get; set; }
}
