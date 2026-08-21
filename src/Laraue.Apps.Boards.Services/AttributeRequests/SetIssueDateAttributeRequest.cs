namespace Laraue.Apps.Boards.Services.AttributeRequests;

public record SetIssueDateAttributeRequest : SetIssueAttributeRequest, ISetIssueScalarAttributeRequest<DateOnly>
{
    public required DateOnly Value { get; set; }
}
