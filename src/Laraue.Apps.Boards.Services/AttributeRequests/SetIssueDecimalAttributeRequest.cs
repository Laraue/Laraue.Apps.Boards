namespace Laraue.Apps.Boards.Services.AttributeRequests;

public record SetIssueDecimalAttributeRequest : SetIssueAttributeRequest, ISetIssueScalarAttributeRequest<decimal>
{
    public required decimal Value { get; set; }
}
