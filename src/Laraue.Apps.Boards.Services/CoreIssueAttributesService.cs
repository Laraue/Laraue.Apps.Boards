using System.Globalization;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
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

public class CoreIssueAttributesService(DatabaseContext context) : ICoreIssueAttributesService
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

        changes.AddRange(
            await UpdateScalarAttributes<IssueAttributeTextValue, string, SetIssueTextAttributeRequest>(
                context.IssueAttributeTextValues,
                issueId,
                attributeNameById,
                attributeRequests.OfType<SetIssueTextAttributeRequest>().ToArray(),
                (issueIdValue, attributeId, value) => new IssueAttributeTextValue
                {
                    IssueId = issueIdValue,
                    AttributeId = attributeId,
                    Value = value,
                },
                value => value,
                cancellationToken));

        changes.AddRange(
            await UpdateScalarAttributes<IssueAttributeIntegerValue, long, SetIssueIntegerAttributeRequest>(
                context.IssueAttributeIntegerValues,
                issueId,
                attributeNameById,
                attributeRequests.OfType<SetIssueIntegerAttributeRequest>().ToArray(),
                (issueIdValue, attributeId, value) => new IssueAttributeIntegerValue
                {
                    IssueId = issueIdValue,
                    AttributeId = attributeId,
                    Value = value,
                },
                value => value.ToString(CultureInfo.InvariantCulture),
                cancellationToken));

        changes.AddRange(
            await UpdateScalarAttributes<IssueAttributeDecimalValue, decimal, SetIssueDecimalAttributeRequest>(
                context.IssueAttributeDecimalValues,
                issueId,
                attributeNameById,
                attributeRequests.OfType<SetIssueDecimalAttributeRequest>().ToArray(),
                (issueIdValue, attributeId, value) => new IssueAttributeDecimalValue
                {
                    IssueId = issueIdValue,
                    AttributeId = attributeId,
                    Value = value,
                },
                value => value.ToString("0.####", CultureInfo.InvariantCulture),
                cancellationToken));

        changes.AddRange(
            await UpdateScalarAttributes<IssueAttributeDateValue, DateOnly, SetIssueDateAttributeRequest>(
                context.IssueAttributeDateValues,
                issueId,
                attributeNameById,
                attributeRequests.OfType<SetIssueDateAttributeRequest>().ToArray(),
                (issueIdValue, attributeId, value) => new IssueAttributeDateValue
                {
                    IssueId = issueIdValue,
                    AttributeId = attributeId,
                    Value = value,
                },
                value => value.ToString("O"),
                cancellationToken));

        changes.AddRange(
            await UpdateScalarAttributes<IssueAttributeDateTimeValue, DateTime, SetIssueDateTimeAttributeRequest>(
                context.IssueAttributeDateTimeValues,
                issueId,
                attributeNameById,
                attributeRequests.OfType<SetIssueDateTimeAttributeRequest>().ToArray(),
                (issueIdValue, attributeId, value) => new IssueAttributeDateTimeValue
                {
                    IssueId = issueIdValue,
                    AttributeId = attributeId,
                    Value = value,
                },
                value => value.ToString("O"),
                cancellationToken));

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

    /// <summary>
    /// Shared insert/update/delete + history-log logic for scalar attribute value types
    /// (Text, Number, Date), which differ only in their storage entity/value type and how the
    /// value is formatted for display. <see cref="IssueAttributeListValue"/> resolves to a
    /// predefined option instead, so it keeps its own <see cref="UpdateListAttributes"/>.
    /// </summary>
    private async Task<OrganizationLogItem[]> UpdateScalarAttributes<TEntity, TValue, TRequest>(
        DbSet<TEntity> dbSet,
        long issueId,
        Dictionary<long, string> attributeNameById,
        TRequest[] attributeRequests,
        Func<long, long, TValue, TEntity> createEntity,
        Func<TValue, string> formatValue,
        CancellationToken cancellationToken)
        where TEntity : class, IIssueAttributeScalarValue<TValue>
        where TRequest : ISetIssueScalarAttributeRequest<TValue>
    {
        var oldAttributes = (await dbSet
                .Where(x => x.IssueId == issueId)
                .Select(x => new { x.AttributeId, x.Value })
                .ToArrayAsyncEF(cancellationToken))
            .ToDictionary(x => x.AttributeId);

        var changes = new List<OrganizationLogItem>();

        if (attributeRequests.Length > 0)
        {
            foreach (var request in attributeRequests)
            {
                // Update old
                if (oldAttributes.TryGetValue(request.Id, out var oldAttribute))
                {
                    if (EqualityComparer<TValue>.Default.Equals(oldAttribute.Value, request.Value))
                        continue;

                    var entity = createEntity(issueId, request.Id, request.Value);

                    dbSet.Attach(entity);
                    context.Entry(entity).State = EntityState.Modified;

                    changes.Add(new OrganizationLogItem
                    {
                        NewDisplayValue = formatValue(request.Value),
                        OldDisplayValue = formatValue(oldAttribute.Value),
                        PropertyType = PropertyType.Attribute,
                        PropertyName = attributeNameById[oldAttribute.AttributeId],
                        ParentId = request.Id.ToString(),
                    });
                }
                // Insert new
                else
                {
                    dbSet.Add(createEntity(issueId, request.Id, request.Value));

                    changes.Add(new OrganizationLogItem
                    {
                        NewDisplayValue = formatValue(request.Value),
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
            .ExceptBy(attributeRequests.Select(x => x.Id), x => x.Key)
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
                OldDisplayValue = formatValue(deletable.Value.Value),
                PropertyType = PropertyType.Attribute,
                PropertyName = attributeNameById[deletable.Key],
                ParentId = deletable.Value.AttributeId.ToString(),
            });
        }

        return changes.ToArray();
    }
}

public abstract record SetIssueAttributeRequest
{
    /// <summary>
    /// The attribute identifier <see cref="DataAccess.Models.Attribute.Id"/>.
    /// </summary>
    public long Id { get; set; }
}

/// <summary>
/// Implemented by <see cref="SetIssueAttributeRequest"/> subtypes for scalar attribute types
/// (Text, Integer, Decimal, Date, DateTime), so they can share
/// <see cref="CoreIssueAttributesService.UpdateScalarAttributes{TEntity,TValue,TRequest}"/>.
/// </summary>
public interface ISetIssueScalarAttributeRequest<out TValue>
{
    long Id { get; }
    TValue Value { get; }
}

public record SetIssueTextAttributeRequest : SetIssueAttributeRequest, ISetIssueScalarAttributeRequest<string>
{
    public required string Value { get; set; }
}

public record SetIssueIntegerAttributeRequest : SetIssueAttributeRequest, ISetIssueScalarAttributeRequest<long>
{
    public required long Value { get; set; }
}

public record SetIssueDecimalAttributeRequest : SetIssueAttributeRequest, ISetIssueScalarAttributeRequest<decimal>
{
    public required decimal Value { get; set; }
}

public record SetIssueDateAttributeRequest : SetIssueAttributeRequest, ISetIssueScalarAttributeRequest<DateOnly>
{
    public required DateOnly Value { get; set; }
}

public record SetIssueDateTimeAttributeRequest : SetIssueAttributeRequest, ISetIssueScalarAttributeRequest<DateTime>
{
    public required DateTime Value { get; set; }
}

public record SetIssueListAttributeRequest : SetIssueAttributeRequest
{
    public required long ListValueId { get; set; }
}
