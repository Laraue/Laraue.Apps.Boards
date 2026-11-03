using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.Attributes;

/// <summary>
/// Which attributes issues have values for, over the six value tables at once, for code that doesn't need the values
/// themselves. A plain read with <b>no permission checks</b>.
/// </summary>
public interface IIssueAttributeValuesReader
{
    /// <summary>
    /// Those of <paramref name="attributeIds"/> that any of <paramref name="issueIds"/> has a value for.
    /// </summary>
    Task<HashSet<long>> GetAttributeIdsWithValues(
        IReadOnlyCollection<long> issueIds,
        IReadOnlyCollection<long> attributeIds,
        CancellationToken cancellationToken);
}

public class IssueAttributeValuesReader(DatabaseContext context) : IIssueAttributeValuesReader
{
    public async Task<HashSet<long>> GetAttributeIdsWithValues(
        IReadOnlyCollection<long> issueIds,
        IReadOnlyCollection<long> attributeIds,
        CancellationToken cancellationToken)
    {
        if (issueIds.Count == 0 || attributeIds.Count == 0)
            return [];

        var attributeIdsWithValues = await AttributeIds(context.IssueAttributeTextValues, issueIds, attributeIds)
            .Concat(AttributeIds(context.IssueAttributeListValues, issueIds, attributeIds))
            .Concat(AttributeIds(context.IssueAttributeIntegerValues, issueIds, attributeIds))
            .Concat(AttributeIds(context.IssueAttributeDecimalValues, issueIds, attributeIds))
            .Concat(AttributeIds(context.IssueAttributeDateValues, issueIds, attributeIds))
            .Concat(AttributeIds(context.IssueAttributeDateTimeValues, issueIds, attributeIds))
            .Distinct()
            .ToArrayAsync(cancellationToken);

        return attributeIdsWithValues.ToHashSet();
    }

    private static IQueryable<long> AttributeIds<TValue>(
        IQueryable<TValue> values,
        IReadOnlyCollection<long> issueIds,
        IReadOnlyCollection<long> attributeIds)
        where TValue : class, IIssueAttributeValue
    {
        return values
            .Where(x => issueIds.Contains(x.IssueId))
            .Where(x => attributeIds.Contains(x.AttributeId))
            .Select(x => x.AttributeId);
    }
}
