using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.AttributeRequests;

namespace Laraue.Apps.Boards.Services.AttributeUpdaters;

/// <summary>
/// Applies changes for one scalar <see cref="AttributeType"/> across an issue's attribute
/// requests. Implemented by <see cref="ScalarAttributeUpdater{TEntity,TValue,TRequest}"/>
/// subclasses - one per scalar type, each registered in DI as <see cref="IScalarAttributeUpdater"/>
/// so <see cref="CoreIssueAttributesService"/> can loop over all of them without knowing about any
/// specific type. <see cref="IssueAttributeListValue"/> resolves to a predefined option instead of
/// storing a value of its own, so it isn't a good fit for this shape and is handled separately.
/// </summary>
public interface IScalarAttributeUpdater
{
    Task<OrganizationLogItem[]> Update(
        DatabaseContext context,
        long issueId,
        Dictionary<long, string> attributeNameById,
        SetIssueAttributeRequest[] attributeRequests,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes the issues' values of <paramref name="attributeIds"/> that are of this updater's type, returning a
    /// history item for each.
    /// </summary>
    Task<DroppedAttributeValue[]> Drop(
        DatabaseContext context,
        IReadOnlyCollection<long> issueIds,
        Dictionary<long, string> attributeNameById,
        IReadOnlyCollection<long> attributeIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// A history item for an attribute value removed from the issue <paramref name="IssueId"/>.
/// </summary>
public sealed record DroppedAttributeValue(long IssueId, OrganizationLogItem LogItem);
