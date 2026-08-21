using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.AttributeRequests;

namespace Laraue.Apps.Boards.Services.AttributeUpdaters;

public class DateTimeAttributeUpdater
    : ScalarAttributeUpdater<IssueAttributeDateTimeValue, DateTime, SetIssueDateTimeAttributeRequest>
{
    protected override IssueAttributeDateTimeValue CreateEntity(long issueId, long attributeId, DateTime value) => new()
    {
        IssueId = issueId,
        AttributeId = attributeId,
        Value = value,
    };

    protected override string FormatValue(DateTime value) => value.ToString("O");
}
