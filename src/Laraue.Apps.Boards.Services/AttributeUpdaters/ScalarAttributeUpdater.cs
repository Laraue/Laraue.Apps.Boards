using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.AttributeRequests;
using Laraue.Core.DataAccess.EFCore.Extensions;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.AttributeUpdaters;

/// <summary>
/// Shared insert/update/delete + history-log logic for one scalar attribute value type
/// (Text, Integer, Decimal, Date, DateTime). Subclasses only need to say how to build the storage
/// entity and how to format the value for display - see <see cref="TextAttributeUpdater"/> etc.
/// </summary>
public abstract class ScalarAttributeUpdater<TEntity, TValue, TRequest> : IScalarAttributeUpdater
    where TEntity : class, IIssueAttributeScalarValue<TValue>
    where TRequest : SetIssueAttributeRequest, ISetIssueScalarAttributeRequest<TValue>
{
    protected abstract TEntity CreateEntity(long issueId, long attributeId, TValue value);

    protected abstract string FormatValue(TValue value);

    public async Task<OrganizationLogItem[]> Update(
        DatabaseContext context,
        long issueId,
        Dictionary<long, string> attributeNameById,
        SetIssueAttributeRequest[] attributeRequests,
        CancellationToken cancellationToken)
    {
        var requests = attributeRequests.OfType<TRequest>().ToArray();
        var dbSet = context.Set<TEntity>();

        var oldAttributes = (await dbSet
                .Where(x => x.IssueId == issueId)
                .Select(x => new { x.AttributeId, x.Value })
                .ToArrayAsyncEF(cancellationToken))
            .ToDictionary(x => x.AttributeId);

        var changes = new List<OrganizationLogItem>();

        if (requests.Length > 0)
        {
            foreach (var request in requests)
            {
                // Update old
                if (oldAttributes.TryGetValue(request.Id, out var oldAttribute))
                {
                    if (EqualityComparer<TValue>.Default.Equals(oldAttribute.Value, request.Value))
                        continue;

                    var entity = CreateEntity(issueId, request.Id, request.Value);

                    dbSet.Attach(entity);
                    context.Entry(entity).State = EntityState.Modified;

                    changes.Add(new OrganizationLogItem
                    {
                        NewDisplayValue = FormatValue(request.Value),
                        OldDisplayValue = FormatValue(oldAttribute.Value),
                        PropertyType = PropertyType.Attribute,
                        PropertyName = attributeNameById[oldAttribute.AttributeId],
                        ParentId = request.Id.ToString(),
                    });
                }
                // Insert new
                else
                {
                    dbSet.Add(CreateEntity(issueId, request.Id, request.Value));

                    changes.Add(new OrganizationLogItem
                    {
                        NewDisplayValue = FormatValue(request.Value),
                        PropertyType = PropertyType.Attribute,
                        PropertyName = attributeNameById[request.Id],
                        ParentId = request.Id.ToString(),
                    });
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        // Drop old
        var toDelete = oldAttributes
            .ExceptBy(requests.Select(x => x.Id), x => x.Key)
            .ToArray();

        if (toDelete.Length != 0)
            await dbSet
                .Where(x => x.IssueId == issueId)
                .Where(x => toDelete.Select(y => y.Key).Contains(x.AttributeId))
                .ExecuteDeleteAsync(cancellationToken);

        foreach (var deletable in toDelete)
        {
            changes.Add(new OrganizationLogItem
            {
                OldDisplayValue = FormatValue(deletable.Value.Value),
                PropertyType = PropertyType.Attribute,
                PropertyName = attributeNameById[deletable.Key],
                ParentId = deletable.Value.AttributeId.ToString(),
            });
        }

        return changes.ToArray();
    }
}
