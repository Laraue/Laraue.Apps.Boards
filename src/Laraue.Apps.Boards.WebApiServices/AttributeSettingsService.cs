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
/// The attribute settings of a space or an epic (BRD-179). Reading needs read access; overriding, saving and resetting
/// need <see cref="AccessLevels.CanUpdateSpace"/> for a space and <see cref="AccessLevels.CanUpdateEpic"/> for an epic.
/// Every change returns the settings in effect after it.
/// </summary>
public interface IAttributeSettingsService
{
    Task<AttributeSettingsDto> GetForSpace(SpaceAttributeSettingsRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The attributes enabled in the space (and its backlog), in the organization's order - for forms and filters.
    /// </summary>
    Task<AttributeDto[]> GetUsedInSpace(SpaceAttributeSettingsRequest request, CancellationToken cancellationToken);
    Task<AttributeSettingsDto> OverrideSpace(SpaceAttributeSettingsRequest request, CancellationToken cancellationToken);
    Task<AttributeSettingsDto> UpdateSpace(UpdateSpaceAttributeSettingsRequest request, CancellationToken cancellationToken);
    Task<AttributeSettingsDto> ResetSpace(SpaceAttributeSettingsRequest request, CancellationToken cancellationToken);

    Task<AttributeSettingsDto> GetForEpic(EpicAttributeSettingsRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The attributes enabled in the epic, in the organization's order - for forms and filters.
    /// </summary>
    Task<AttributeDto[]> GetUsedInEpic(EpicAttributeSettingsRequest request, CancellationToken cancellationToken);
    Task<AttributeSettingsDto> OverrideEpic(EpicAttributeSettingsRequest request, CancellationToken cancellationToken);
    Task<AttributeSettingsDto> UpdateEpic(UpdateEpicAttributeSettingsRequest request, CancellationToken cancellationToken);
    Task<AttributeSettingsDto> ResetEpic(EpicAttributeSettingsRequest request, CancellationToken cancellationToken);
}

public class AttributeSettingsService(
    DatabaseContext context,
    IAccessService accessService,
    ICoreSpacesService coreSpacesService,
    ICoreAttributeSettingsService coreAttributeSettingsService,
    IAttributeSettingsReader attributeSettingsReader)
    : IAttributeSettingsService
{
    public async Task<AttributeSettingsDto> GetForSpace(
        SpaceAttributeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var spaceId = await GetSpaceId(request.AuthData, request.Key, canUpdate: false, cancellationToken);
        return await attributeSettingsReader.GetForSpace(spaceId, cancellationToken);
    }

    public async Task<AttributeDto[]> GetUsedInSpace(
        SpaceAttributeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var spaceId = await GetSpaceId(request.AuthData, request.Key, canUpdate: false, cancellationToken);
        var settings = await attributeSettingsReader.GetForSpace(spaceId, cancellationToken);
        return await ToUsedAttributes(settings, cancellationToken);
    }

    public async Task<AttributeSettingsDto> OverrideSpace(
        SpaceAttributeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var spaceId = await GetSpaceId(request.AuthData, request.Key, canUpdate: true, cancellationToken);
        return await ChangeSpace(
            spaceId,
            () => coreAttributeSettingsService.OverrideSpace(spaceId, cancellationToken),
            cancellationToken);
    }

    public async Task<AttributeSettingsDto> UpdateSpace(
        UpdateSpaceAttributeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var spaceId = await GetSpaceId(request.AuthData, request.Key, canUpdate: true, cancellationToken);
        return await ChangeSpace(
            spaceId,
            () => coreAttributeSettingsService.UpdateSpace(spaceId, ToChanges(request.Settings), cancellationToken),
            cancellationToken);
    }

    public async Task<AttributeSettingsDto> ResetSpace(
        SpaceAttributeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var spaceId = await GetSpaceId(request.AuthData, request.Key, canUpdate: true, cancellationToken);
        return await ChangeSpace(
            spaceId,
            () => coreAttributeSettingsService.ResetSpace(spaceId, cancellationToken),
            cancellationToken);
    }

    public async Task<AttributeSettingsDto> GetForEpic(
        EpicAttributeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureEpicAccess(request.AuthData, request.Id, canUpdate: false, cancellationToken);
        return await attributeSettingsReader.GetForEpic(request.Id, cancellationToken);
    }

    public async Task<AttributeDto[]> GetUsedInEpic(
        EpicAttributeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureEpicAccess(request.AuthData, request.Id, canUpdate: false, cancellationToken);
        var settings = await attributeSettingsReader.GetForEpic(request.Id, cancellationToken);
        return await ToUsedAttributes(settings, cancellationToken);
    }

    public async Task<AttributeSettingsDto> OverrideEpic(
        EpicAttributeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureEpicAccess(request.AuthData, request.Id, canUpdate: true, cancellationToken);
        return await ChangeEpic(
            request.Id,
            () => coreAttributeSettingsService.OverrideEpic(request.Id, cancellationToken),
            cancellationToken);
    }

    public async Task<AttributeSettingsDto> UpdateEpic(
        UpdateEpicAttributeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureEpicAccess(request.AuthData, request.Id, canUpdate: true, cancellationToken);
        return await ChangeEpic(
            request.Id,
            () => coreAttributeSettingsService.UpdateEpic(request.Id, ToChanges(request.Settings), cancellationToken),
            cancellationToken);
    }

    public async Task<AttributeSettingsDto> ResetEpic(
        EpicAttributeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureEpicAccess(request.AuthData, request.Id, canUpdate: true, cancellationToken);
        return await ChangeEpic(
            request.Id,
            () => coreAttributeSettingsService.ResetEpic(request.Id, cancellationToken),
            cancellationToken);
    }

    private async Task<long> GetSpaceId(
        OrganizationAuthData authData,
        string key,
        bool canUpdate,
        CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(authData.OrganizationId, key, cancellationToken);

        var accessLevels = await accessService
            .GetAccessLevelsBySpaceId(authData, spaceId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Space", key))
            .EnsureOrThrowNotFound(
                a => a.CanRead,
                string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Space", key));
        if (canUpdate && !accessLevels.CanUpdateSpace)
        {
            throw new ForbiddenException(
                string.Format(ErrorMessages.EntityActionForbidden, "Space", key, "update"));
        }

        return spaceId;
    }

    private async Task EnsureEpicAccess(
        OrganizationAuthData authData,
        long epicId,
        bool canUpdate,
        CancellationToken cancellationToken)
    {
        var accessLevels = await accessService
            .GetAccessLevelsByEpicId(authData, epicId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Epic", epicId))
            .EnsureOrThrowNotFound(
                a => a.CanRead,
                string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Epic", epicId));
        if (canUpdate && !accessLevels.CanUpdateEpic)
        {
            throw new ForbiddenException(
                string.Format(ErrorMessages.EntityActionForbidden, "Epic", epicId, "update"));
        }
    }

    private async Task<AttributeSettingsDto> ChangeSpace(
        long spaceId,
        Func<Task> change,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await change();
        await transaction.CommitAsync(cancellationToken);

        return await attributeSettingsReader.GetForSpace(spaceId, cancellationToken);
    }

    private async Task<AttributeSettingsDto> ChangeEpic(
        long epicId,
        Func<Task> change,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await change();
        await transaction.CommitAsync(cancellationToken);

        return await attributeSettingsReader.GetForEpic(epicId, cancellationToken);
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

    private static AttributeSettingChange[] ToChanges(AttributeSettingInput[] settings)
    {
        return settings
            .Select(x => new AttributeSettingChange(x.AttributeId, x.IsEnabled, x.ShowOnCard))
            .ToArray();
    }
}

public record SpaceAttributeSettingsRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public required string Key { get; set; }
}

public record UpdateSpaceAttributeSettingsRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The space's settings for the organization's attributes; attributes left out are off.
    /// </summary>
    public required AttributeSettingInput[] Settings { get; set; }
}

public record EpicAttributeSettingsRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public long Id { get; set; }
}

public record UpdateEpicAttributeSettingsRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public long Id { get; set; }

    /// <summary>
    /// The epic's settings for the organization's attributes; attributes left out are off.
    /// </summary>
    public required AttributeSettingInput[] Settings { get; set; }
}

public record AttributeSettingInput
{
    public required long AttributeId { get; set; }
    public required bool IsEnabled { get; set; }
    public required bool ShowOnCard { get; set; }
}
