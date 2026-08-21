using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.AttributeRequests;

namespace Laraue.Apps.Boards.Services.AttributeUpdaters;

public class DateAttributeUpdater
    : ScalarAttributeUpdater<IssueAttributeDateValue, DateOnly, SetIssueDateAttributeRequest>
{
    protected override IssueAttributeDateValue CreateEntity(long issueId, long attributeId, DateOnly value) => new()
    {
        IssueId = issueId,
        AttributeId = attributeId,
        Value = value,
    };

    protected override string FormatValue(DateOnly value) => value.ToString("O");
}
