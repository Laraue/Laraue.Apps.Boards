using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Attributes;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.WebApiServices;

/// <summary>
/// Reads the attribute settings of a space or an epic (BRD-179), with read access to it. They're saved with the rest of
/// the space's or epic's settings: <see cref="UpdateSpaceRequest.AttributeSettings"/>,
/// <see cref="UpdateEpicRequest.AttributeSettings"/>.
/// </summary>
public interface IAttributeSettingsService
{
    Task<AttributeSettingsViewDto> GetForSpace(SpaceAttributeSettingsRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The attributes enabled in the space (and its backlog), in the organization's order - for forms and filters.
    /// </summary>
    Task<AttributeDto[]> GetUsedInSpace(SpaceAttributeSettingsRequest request, CancellationToken cancellationToken);

    Task<AttributeSettingsViewDto> GetForEpic(EpicAttributeSettingsRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The attributes enabled in the epic, in the organization's order - for forms and filters.
    /// </summary>
    Task<AttributeDto[]> GetUsedInEpic(EpicAttributeSettingsRequest request, CancellationToken cancellationToken);
}

public class AttributeSettingsService(
    DatabaseContext context,
    IAccessService accessService,
    ICoreSpacesService coreSpacesService,
    IAttributeSettingsReader attributeSettingsReader)
    : IAttributeSettingsService
{
    public async Task<AttributeSettingsViewDto> GetForSpace(
        SpaceAttributeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var spaceId = await GetSpaceId(request.AuthData, request.Key, cancellationToken);
        var settings = await attributeSettingsReader.GetForSpace(spaceId, cancellationToken);
        return new AttributeSettingsViewDto
        {
            Source = settings.Source,
            Attributes = settings.Attributes,
            Inherited = await attributeSettingsReader.GetInheritedForSpace(spaceId, cancellationToken),
        };
    }

    public async Task<AttributeDto[]> GetUsedInSpace(
        SpaceAttributeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var spaceId = await GetSpaceId(request.AuthData, request.Key, cancellationToken);
        var settings = await attributeSettingsReader.GetForSpace(spaceId, cancellationToken);
        return await ToUsedAttributes(settings, cancellationToken);
    }

    public async Task<AttributeSettingsViewDto> GetForEpic(
        EpicAttributeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureEpicAccess(request.AuthData, request.Id, cancellationToken);
        var settings = await attributeSettingsReader.GetForEpic(request.Id, cancellationToken);
        return new AttributeSettingsViewDto
        {
            Source = settings.Source,
            Attributes = settings.Attributes,
            Inherited = await attributeSettingsReader.GetInheritedForEpic(request.Id, cancellationToken),
        };
    }

    public async Task<AttributeDto[]> GetUsedInEpic(
        EpicAttributeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureEpicAccess(request.AuthData, request.Id, cancellationToken);
        var settings = await attributeSettingsReader.GetForEpic(request.Id, cancellationToken);
        return await ToUsedAttributes(settings, cancellationToken);
    }

    private async Task<long> GetSpaceId(
        OrganizationAuthData authData,
        string key,
        CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(authData.OrganizationId, key, cancellationToken);

        await accessService
            .GetAccessLevelsBySpaceId(authData, spaceId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Space", key))
            .EnsureOrThrowNotFound(
                a => a.CanRead,
                string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Space", key));

        return spaceId;
    }

    private async Task EnsureEpicAccess(
        OrganizationAuthData authData,
        long epicId,
        CancellationToken cancellationToken)
    {
        await accessService
            .GetAccessLevelsByEpicId(authData, epicId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Epic", epicId))
            .EnsureOrThrowNotFound(
                a => a.CanRead,
                string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Epic", epicId));
    }

    private async Task<AttributeDto[]> ToUsedAttributes(
        AttributeSettingsDto settings,
        CancellationToken cancellationToken)
    {
        var used = settings.Attributes
            .Where(x => x.IsEnabled)
            .ToArray();
        var listAttributeIds = used
            .Where(x => x.Type == AttributeType.List)
            .Select(x => x.AttributeId)
            .ToArray();
        var listValuesByAttributeId = (await context.AttributeListValues
                .Where(x => listAttributeIds.Contains(x.AttributeId))
                .OrderBy(x => x.Id)
                .Select(x => new { x.AttributeId, x.Id, x.Value })
                .ToArrayAsync(cancellationToken))
            .GroupBy(x => x.AttributeId)
            .ToDictionary(
                x => x.Key,
                x => x
                    .Select(v => new AttributeListValueDto { Id = v.Id, Name = v.Value })
                    .ToArray());

        return used
            .Select(x => new AttributeDto
            {
                Id = x.AttributeId,
                Name = x.Name,
                Color = x.Color,
                Type = x.Type,
                IsEnabled = x.IsEnabled,
                ShowOnCard = x.ShowOnCard,
                ListValues = listValuesByAttributeId.GetValueOrDefault(x.AttributeId, []),
            })
            .ToArray();
    }
}

/// <summary>
/// A space's or an epic's attribute settings in effect, and what it would follow without its own - to show when its
/// own settings are being dropped.
/// </summary>
public record AttributeSettingsViewDto
{
    public required AttributeSettingsSource Source { get; init; }
    public required AttributeSettingDto[] Attributes { get; init; }
    public required AttributeSettingsDto Inherited { get; init; }
}

public record SpaceAttributeSettingsRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public required string Key { get; set; }
}

public record EpicAttributeSettingsRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public long Id { get; set; }
}

/// <summary>
/// A space's or an epic's attribute settings as saved with the rest of its settings.
/// </summary>
public record AttributeSettingsUpdate
{
    /// <summary>
    /// Whether the space or epic has its own settings - <see cref="Settings"/> - or follows the level above.
    /// </summary>
    public required bool Overridden { get; set; }

    /// <summary>
    /// The own settings for the organization's attributes; attributes left out are off. Ignored unless
    /// <see cref="Overridden"/>.
    /// </summary>
    public AttributeSettingInput[] Settings { get; set; } = [];

    /// <summary>
    /// What the core service saves: null to follow the level above.
    /// </summary>
    public AttributeSettingChange[]? ToChanges()
    {
        return Overridden
            ? Settings
                .Select(x => new AttributeSettingChange(x.AttributeId, x.IsEnabled, x.ShowOnCard))
                .ToArray()
            : null;
    }
}

public record AttributeSettingInput
{
    public required long AttributeId { get; set; }
    public required bool IsEnabled { get; set; }
    public required bool ShowOnCard { get; set; }
}
