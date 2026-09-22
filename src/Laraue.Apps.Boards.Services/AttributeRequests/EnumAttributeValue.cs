namespace Laraue.Apps.Boards.Services.AttributeRequests;

public record EnumAttributeValue : AttributeValue
{
    public required long ValueId { get; set; }
}
