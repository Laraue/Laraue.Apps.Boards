namespace Laraue.Apps.Boards.Services.AttributeRequests;

public record DateAttributeValue : AttributeValue
{
    public required DateOnly Value { get; set; }
}
