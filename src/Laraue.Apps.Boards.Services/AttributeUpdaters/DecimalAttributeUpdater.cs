using System.Globalization;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.AttributeRequests;

namespace Laraue.Apps.Boards.Services.AttributeUpdaters;

public class DecimalAttributeUpdater
    : ScalarAttributeUpdater<IssueAttributeDecimalValue, decimal, SetIssueDecimalAttributeRequest>
{
    protected override IssueAttributeDecimalValue CreateEntity(long issueId, long attributeId, decimal value) => new()
    {
        IssueId = issueId,
        AttributeId = attributeId,
        Value = value,
    };

    protected override string FormatValue(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
