using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.AttributeRequests;

namespace Laraue.Apps.Boards.Services.AttributeUpdaters;

public class TextAttributeUpdater
    : ScalarAttributeUpdater<IssueAttributeTextValue, string, SetIssueTextAttributeRequest>
{
    protected override IssueAttributeTextValue CreateEntity(long issueId, long attributeId, string value) => new()
    {
        IssueId = issueId,
        AttributeId = attributeId,
        Value = value,
    };

    protected override string FormatValue(string value) => value;
}
