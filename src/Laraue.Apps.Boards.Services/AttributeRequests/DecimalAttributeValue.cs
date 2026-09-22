namespace Laraue.Apps.Boards.Services.AttributeRequests;

public record DecimalAttributeValue : AttributeValue
{
    public required decimal Value { get; set; }
}
