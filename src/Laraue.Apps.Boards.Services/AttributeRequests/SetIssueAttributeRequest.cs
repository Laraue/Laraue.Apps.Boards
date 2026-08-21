namespace Laraue.Apps.Boards.Services.AttributeRequests;

public abstract record SetIssueAttributeRequest
{
    /// <summary>
    /// The attribute identifier <see cref="DataAccess.Models.Attribute.Id"/>.
    /// </summary>
    public long Id { get; set; }
}
