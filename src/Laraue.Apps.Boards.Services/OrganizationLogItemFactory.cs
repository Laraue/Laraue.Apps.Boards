using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Builds <see cref="OrganizationLogItem"/>s for individual property changes. Pure - has no
/// dependency on the database, unlike <see cref="IIssueHistoryService"/> which persists them.
/// </summary>
public interface IOrganizationLogItemFactory
{
    OrganizationLogItem ContentChanged(string? oldValue, string? newValue);

    OrganizationLogItem AttachmentAdded(Guid? previewFileId, string? fileName);

    OrganizationLogItem AttachmentRemoved(Guid? previewFileId, string? fileName);

    OrganizationLogItem AssigneeChanged(IdName<Guid>? oldValue, IdName<Guid>? newValue);

    OrganizationLogItem SpaceChanged(IdName<long>? oldValue, IdName<long>? newValue);

    OrganizationLogItem EpicChanged(IdName<long>? oldValue, IdName<long>? newValue);

    OrganizationLogItem StatusChanged(IdName<long>? oldValue, IdName<long>? newValue);
}

public class OrganizationLogItemFactory : IOrganizationLogItemFactory
{
    public OrganizationLogItem ContentChanged(string? oldValue, string? newValue)
    {
        return new OrganizationLogItem
        {
            NewDisplayValue = newValue,
            OldDisplayValue = oldValue,
            PropertyType = PropertyType.Content,
        };
    }

    public OrganizationLogItem AttachmentAdded(Guid? previewFileId, string? fileName)
    {
        return new OrganizationLogItem
        {
            PropertyType = PropertyType.Attachment,
            NewValueId = previewFileId.ToString(),
            NewDisplayValue = fileName,
        };
    }

    public OrganizationLogItem AttachmentRemoved(Guid? previewFileId, string? fileName)
    {
        return new OrganizationLogItem
        {
            PropertyType = PropertyType.Attachment,
            OldValueId = previewFileId.ToString(),
            OldDisplayValue = fileName,
        };
    }

    public OrganizationLogItem AssigneeChanged(IdName<Guid>? oldValue, IdName<Guid>? newValue)
        => GetLogItem(PropertyType.Assignee, oldValue, newValue);

    public OrganizationLogItem SpaceChanged(IdName<long>? oldValue, IdName<long>? newValue)
        => GetLogItem(PropertyType.Space, oldValue, newValue);

    public OrganizationLogItem EpicChanged(IdName<long>? oldValue, IdName<long>? newValue)
        => GetLogItem(PropertyType.Epic, oldValue, newValue);

    public OrganizationLogItem StatusChanged(IdName<long>? oldValue, IdName<long>? newValue)
        => GetLogItem(PropertyType.Status, oldValue, newValue);

    private static OrganizationLogItem GetLogItem<T>(
        PropertyType propertyType,
        IdName<T>? oldValue,
        IdName<T>? newValue) where T : struct
    {
        var item = new OrganizationLogItem
        {
            PropertyType = propertyType,
            NewDisplayValue = newValue?.Name,
            OldDisplayValue = oldValue?.Name,
        };

        if (oldValue.HasValue)
            item.OldValueId = oldValue.Value.Id.ToString();

        if (newValue.HasValue)
            item.NewValueId = newValue.Value.Id.ToString();

        return item;
    }
}

public record struct IdName<T>(T Id, string Name) where T : struct;
