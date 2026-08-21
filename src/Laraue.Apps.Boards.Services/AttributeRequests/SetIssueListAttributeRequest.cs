namespace Laraue.Apps.Boards.Services.AttributeRequests;

public record SetIssueListAttributeRequest : SetIssueAttributeRequest
{
    public required long ListValueId { get; set; }
}
