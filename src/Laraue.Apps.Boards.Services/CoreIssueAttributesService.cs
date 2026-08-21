using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.AttributeRequests;
using Laraue.Apps.Boards.Services.AttributeUpdaters;
using Laraue.Core.DataAccess.EFCore.Extensions;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

public interface ICoreIssueAttributesService
{
    /// <summary>
    /// Applies attribute value changes (of any <see cref="AttributeType"/>) to an issue and
    /// returns the resulting history log items. Values omitted from <paramref name="attributeRequests"/>
    /// but currently set on the issue are removed.
    /// </summary>
    Task<OrganizationLogItem[]> UpdateAttributes(
        long issueId,
        long organizationId,
        SetIssueAttributeRequest[] attributeRequests,
        CancellationToken cancellationToken);
}

public class CoreIssueAttributesService(
    DatabaseContext context,
    IEnumerable<IScalarAttributeUpdater> scalarAttributeUpdaters)
    : ICoreIssueAttributesService
{
    public async Task<OrganizationLogItem[]> UpdateAttributes(
        long issueId,
        long organizationId,
        SetIssueAttributeRequest[] attributeRequests,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var changes = new List<OrganizationLogItem>();

        var attributeNameById = await context.Attributes
            .Where(x => x.OrganizationId == organizationId)
            .ToDictionaryAsyncEF(x => x.Id, x => x.Name, cancellationToken);

        // One updater per scalar AttributeType (see AttributeUpdaters/), each registered in DI.
        // IssueAttributeListValue resolves to a predefined option instead of storing a value of
        // its own, so it isn't a good fit for this shape and keeps UpdateListAttributes below.
        foreach (var updater in scalarAttributeUpdaters)
        {
            changes.AddRange(
                await updater.Update(context, issueId, attributeNameById, attributeRequests, cancellationToken));
        }

        changes.AddRange(
            await UpdateListAttributes(
                issueId,
                attributeNameById,
                attributeRequests.OfType<SetIssueListAttributeRequest>().ToArray(),
                cancellationToken));

        return changes.ToArray();
    }

    private async Task<OrganizationLogItem[]> UpdateListAttributes(
        long issueId,
        Dictionary<long, string> attributeNameById,
        SetIssueListAttributeRequest[] attributeRequests,
        CancellationToken cancellationToken)
    {
        var oldAttributes = await context.IssueAttributeListValues
            .Where(x => x.IssueId == issueId)
            .Select(x => new
            {
                x.Id,
                x.AttributeId,
                x.AttributeListValueId,
                AttributeListValue = x.AttributeListValue!.Value,
                x.Attribute!.Color,
            })
            .ToArrayAsyncEF(cancellationToken);

        var oldAttributeById =  oldAttributes
            .ToDictionary(x => x.AttributeId);

        var changes = new List<OrganizationLogItem>();

        if (attributeRequests.Length > 0)
        {
            var valueNames = await context.AttributeListValues
                .Where(x => attributeRequests.Select(y => y.Id).Contains(x.AttributeId))
                .Select(x => new { x.Id, x.AttributeId, x.Value, x.Attribute!.Color })
                .ToArrayAsyncEF(cancellationToken);

            var valueByAttributeId = valueNames
                .GroupBy(x => x.AttributeId)
                .ToDictionary(
                    x => x.Key,
                    x => x.ToDictionary(y => y.Id));

            foreach (var request in attributeRequests)
            {
                var listValueData = valueByAttributeId[request.Id][request.ListValueId];

                // Update old
                if (oldAttributeById.TryGetValue(request.Id, out var oldAttribute))
                {
                    if (oldAttribute.AttributeListValueId == request.ListValueId)
                        continue;

                    var entity = new IssueAttributeListValue
                    {
                        Id = oldAttribute.Id,
                        IssueId = issueId,
                        AttributeId = oldAttribute.AttributeId,
                        AttributeListValueId = request.ListValueId,
                    };

                    context.Attach(entity);
                    context.Entry(entity).State = EntityState.Modified;

                    changes.Add(new OrganizationLogItem
                    {
                        NewDisplayValue = listValueData.Value,
                        OldDisplayValue = oldAttribute.AttributeListValue,
                        PropertyType = PropertyType.Attribute,
                        OldValueId = oldAttribute.AttributeListValueId.ToString(),
                        NewValueId = request.ListValueId.ToString(),
                        PropertyName = attributeNameById[request.Id],
                        ParentId = request.Id.ToString(),
                    });
                }
                // Insert new
                else
                {
                    context.Add(new IssueAttributeListValue
                    {
                        AttributeId = request.Id,
                        IssueId = issueId,
                        AttributeListValueId = request.ListValueId,
                    });

                    changes.Add(new OrganizationLogItem
                    {
                        NewDisplayValue = listValueData.Value,
                        PropertyType = PropertyType.Attribute,
                        NewValueId = request.ListValueId.ToString(),
                        PropertyName = attributeNameById[request.Id],
                        ParentId = request.Id.ToString(),
                    });
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        // Drop old
        var toDelete = oldAttributeById.Keys
            .Except(attributeRequests.Select(x => x.Id))
            .ToArray();

        if (toDelete.Length != 0)
        {
            var deletableValues = await context.IssueAttributeListValues
                .Where(x => x.IssueId == issueId)
                .Where(x => ((IEnumerable<long>)toDelete).Contains(x.AttributeId))
                .Select(x => new
                {
                    x.Id,
                    x.AttributeId,
                    AttributeListValueName = x.AttributeListValue!.Value,
                    x.AttributeListValueId,
                })
                .ToDictionaryAsyncEF(x => x.Id, cancellationToken);

            foreach (var deletableValue in deletableValues)
            {
                changes.Add(new OrganizationLogItem
                {
                    OldDisplayValue = deletableValue.Value.AttributeListValueName,
                    PropertyType = PropertyType.Attribute,
                    OldValueId = deletableValue.Value.AttributeListValueId.ToString(),
                    PropertyName = attributeNameById[deletableValue.Key],
                    ParentId = deletableValue.Value.AttributeId.ToString(),
                });
            }

            await context.IssueAttributeListValues
                .Where(x => deletableValues.Select(v => v.Key).Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        return changes.ToArray();
    }
}
