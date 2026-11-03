using Laraue.Apps.Boards.DataAccess.Models;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.DataAccess;

/// <summary>
/// The six issue attribute value tables seen as one, for code that only needs which issue has a value for which
/// attribute, not the value itself.
/// </summary>
public static class DatabaseContextIssueAttributeValueExtensions
{
    /// <summary>
    /// Every (issue, attribute) pair that has a value, of any type - one query to filter further.
    /// </summary>
    public static IQueryable<IssueAttributeValueKey> IssueAttributeValueKeys(this DatabaseContext context) =>
        Keys(context.IssueAttributeTextValues)
            .Concat(Keys(context.IssueAttributeListValues))
            .Concat(Keys(context.IssueAttributeIntegerValues))
            .Concat(Keys(context.IssueAttributeDecimalValues))
            .Concat(Keys(context.IssueAttributeDateValues))
            .Concat(Keys(context.IssueAttributeDateTimeValues));

    /// <summary>
    /// Deletes every attribute value, of any type, of <paramref name="issueIds"/>.
    /// </summary>
    public static async Task DeleteIssueAttributeValues(
        this DatabaseContext context,
        IReadOnlyCollection<long> issueIds,
        CancellationToken cancellationToken)
    {
        await Delete(context.IssueAttributeTextValues, issueIds, cancellationToken);
        await Delete(context.IssueAttributeListValues, issueIds, cancellationToken);
        await Delete(context.IssueAttributeIntegerValues, issueIds, cancellationToken);
        await Delete(context.IssueAttributeDecimalValues, issueIds, cancellationToken);
        await Delete(context.IssueAttributeDateValues, issueIds, cancellationToken);
        await Delete(context.IssueAttributeDateTimeValues, issueIds, cancellationToken);
    }

    private static IQueryable<IssueAttributeValueKey> Keys<TValue>(IQueryable<TValue> values)
        where TValue : class, IIssueAttributeValue =>
        values.Select(x => new IssueAttributeValueKey { IssueId = x.IssueId, AttributeId = x.AttributeId });

    private static Task<int> Delete<TValue>(
        IQueryable<TValue> values,
        IReadOnlyCollection<long> issueIds,
        CancellationToken cancellationToken)
        where TValue : class, IIssueAttributeValue =>
        values
            .Where(x => issueIds.Contains(x.IssueId))
            .ExecuteDeleteAsync(cancellationToken);
}

public sealed record IssueAttributeValueKey
{
    public required long IssueId { get; init; }
    public required long AttributeId { get; init; }
}
