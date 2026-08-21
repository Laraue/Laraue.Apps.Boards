using System.Globalization;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.AttributeRequests;

namespace Laraue.Apps.Boards.Services.AttributeUpdaters;

public class IntegerAttributeUpdater
    : ScalarAttributeUpdater<IssueAttributeIntegerValue, long, SetIssueIntegerAttributeRequest>
{
    protected override IssueAttributeIntegerValue CreateEntity(long issueId, long attributeId, long value) => new()
    {
        IssueId = issueId,
        AttributeId = attributeId,
        Value = value,
    };

    protected override string FormatValue(long value) => value.ToString(CultureInfo.InvariantCulture);
}
