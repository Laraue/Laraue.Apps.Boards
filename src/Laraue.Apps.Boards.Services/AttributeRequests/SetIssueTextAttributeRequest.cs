namespace Laraue.Apps.Boards.Services.AttributeRequests;

public record SetIssueTextAttributeRequest : SetIssueAttributeRequest, ISetIssueScalarAttributeRequest<string>
{
    public required string Value { get; set; }
}
