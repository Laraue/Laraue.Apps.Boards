namespace Laraue.Apps.Boards.Services.AttributeRequests;

public record SetIssueDateTimeAttributeRequest : SetIssueAttributeRequest, ISetIssueScalarAttributeRequest<DateTime>
{
    public required DateTime Value { get; set; }
}
