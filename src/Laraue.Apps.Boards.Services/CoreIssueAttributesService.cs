using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.AttributeRequests;
using Laraue.Apps.Boards.Services.AttributeUpdaters;
using Laraue.Core.Exceptions.Web;
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

    /// <summary>
    /// Validates <paramref name="attributeValues"/> (each already resolved to an attribute id -
    /// by the REST API's own typed request body, or by the MCP host after parsing a caller's
    /// plain text) against the organization's actual attribute definitions, and builds the
    /// corresponding <see cref="SetIssueAttributeRequest"/>s for <see cref="UpdateAttributes"/>.
    /// Collects every problem found (unknown attribute, wrong value shape, too-long text, unknown
    /// list value id) rather than failing on the first one, then throws a single
    /// <see cref="BadRequestException"/> if any were found - same shape both hosts' callers
    /// already return to their own clients.
    /// </summary>
    Task<SetIssueAttributeRequest[]> BuildSetRequests(
        long organizationId,
        AttributeValue[] attributeValues,
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

    public async Task<SetIssueAttributeRequest[]> BuildSetRequests(
        long organizationId,
        AttributeValue[] attributeValues,
        CancellationToken cancellationToken)
    {
        if (attributeValues.Length == 0)
            return [];

        var uniqueValues = attributeValues
            .DistinctBy(x => x.AttributeId)
            .ToArray();

        var attributeTypeById = await context.Attributes
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => uniqueValues.Select(v => v.AttributeId).Contains(x.Id))
            .ToDictionaryAsyncEF(x => x.Id, x => x.AttributeType, cancellationToken);

        var listAttributeIds = uniqueValues
            .Where(v => attributeTypeById.GetValueOrDefault(v.AttributeId) == AttributeType.List)
            .Select(v => v.AttributeId)
            .ToArray();

        // Batched existence check for every requested list value id, keyed by (AttributeId,
        // ListValueId) so a request can't silently point at another attribute's option.
        var validListValueKeys = new HashSet<(long AttributeId, long ValueId)>();
        if (listAttributeIds.Length > 0)
        {
            var listValues = await context.AttributeListValues
                .Where(v => listAttributeIds.Contains(v.AttributeId))
                .Select(v => new { v.AttributeId, v.Id })
                .ToArrayAsyncEF(cancellationToken);

            foreach (var v in listValues)
                validListValueKeys.Add((v.AttributeId, v.Id));
        }

        var requests = new List<SetIssueAttributeRequest>();
        var errors = new List<string?>();

        foreach (var attributeValue in uniqueValues)
        {
            if (!attributeTypeById.TryGetValue(attributeValue.AttributeId, out var attributeType))
            {
                errors.Add($"Attribute: {attributeValue.AttributeId} is not found");
                continue;
            }

            switch (attributeType)
            {
                case AttributeType.List:
                    if (attributeValue is not EnumAttributeValue enumAttributeValue)
                    {
                        errors.Add($"Attribute: {attributeValue.AttributeId} should be an enum value");
                        continue;
                    }

                    if (!validListValueKeys.Contains((enumAttributeValue.AttributeId, enumAttributeValue.ValueId)))
                    {
                        errors.Add($"Attribute: {attributeValue.AttributeId} has no list value {enumAttributeValue.ValueId}");
                        continue;
                    }

                    requests.Add(new SetIssueListAttributeRequest
                    {
                        Id = enumAttributeValue.AttributeId,
                        ListValueId = enumAttributeValue.ValueId,
                    });
                    break;

                case AttributeType.Text:
                    if (attributeValue is not StringAttributeValue stringAttributeValue)
                    {
                        errors.Add($"Attribute: {attributeValue.AttributeId} should be a string value");
                        continue;
                    }

                    if (stringAttributeValue.Value.Length > 255)
                    {
                        errors.Add($"Attribute: {attributeValue.AttributeId} value must be at most 255 characters");
                        continue;
                    }

                    requests.Add(new SetIssueTextAttributeRequest
                    {
                        Id = stringAttributeValue.AttributeId,
                        Value = stringAttributeValue.Value,
                    });
                    break;

                case AttributeType.Integer:
                    if (attributeValue is not IntegerAttributeValue integerAttributeValue)
                    {
                        errors.Add($"Attribute: {attributeValue.AttributeId} should be an integer value");
                        continue;
                    }

                    requests.Add(new SetIssueIntegerAttributeRequest
                    {
                        Id = integerAttributeValue.AttributeId,
                        Value = integerAttributeValue.Value,
                    });
                    break;

                case AttributeType.Decimal:
                    if (attributeValue is not DecimalAttributeValue decimalAttributeValue)
                    {
                        errors.Add($"Attribute: {attributeValue.AttributeId} should be a decimal value");
                        continue;
                    }

                    requests.Add(new SetIssueDecimalAttributeRequest
                    {
                        Id = decimalAttributeValue.AttributeId,
                        Value = decimalAttributeValue.Value,
                    });
                    break;

                case AttributeType.Date:
                    if (attributeValue is not DateAttributeValue dateAttributeValue)
                    {
                        errors.Add($"Attribute: {attributeValue.AttributeId} should be a date value");
                        continue;
                    }

                    requests.Add(new SetIssueDateAttributeRequest
                    {
                        Id = dateAttributeValue.AttributeId,
                        Value = dateAttributeValue.Value,
                    });
                    break;

                case AttributeType.DateTime:
                    if (attributeValue is not DateTimeAttributeValue dateTimeAttributeValue)
                    {
                        errors.Add($"Attribute: {attributeValue.AttributeId} should be a date-time value");
                        continue;
                    }

                    requests.Add(new SetIssueDateTimeAttributeRequest
                    {
                        Id = dateTimeAttributeValue.AttributeId,
                        Value = dateTimeAttributeValue.Value,
                    });
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(attributeType), attributeType, null);
            }
        }

        if (errors.Count > 0)
            throw new BadRequestException(new Dictionary<string, string?[]>
            {
                [nameof(attributeValues)] = errors.ToArray(),
            });

        return requests.ToArray();
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
