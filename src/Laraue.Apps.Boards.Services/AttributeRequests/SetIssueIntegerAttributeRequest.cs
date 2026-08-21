namespace Laraue.Apps.Boards.Services.AttributeRequests;

public record SetIssueIntegerAttributeRequest : SetIssueAttributeRequest, ISetIssueScalarAttributeRequest<long>
{
    public required long Value { get; set; }
}
