namespace Laraue.Apps.Boards.Services.AttributeRequests;

public record DateTimeAttributeValue : AttributeValue
{
    public required DateTime Value { get; set; }
}
