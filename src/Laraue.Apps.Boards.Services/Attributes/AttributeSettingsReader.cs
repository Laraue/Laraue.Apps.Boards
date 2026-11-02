using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.Attributes;

/// <summary>
/// The attribute settings in effect for a space or an epic (BRD-179): the epic's own when it overrides them, else its
/// space's, else the organization's defaults on <see cref="DataAccess.Models.Attribute"/>. A plain read with <b>no
/// permission checks</b>: the caller checks access to the space or epic.
/// </summary>
public interface IAttributeSettingsReader
{
    /// <summary>
    /// The space's settings - also the settings of its backlog, which never overrides them.
    /// </summary>
    Task<AttributeSettingsDto> GetForSpace(long spaceId, CancellationToken cancellationToken);

    /// <summary>
    /// The settings issues of the epic follow.
    /// </summary>
    Task<AttributeSettingsDto> GetForEpic(long epicId, CancellationToken cancellationToken);
}

/// <summary>
/// Where the settings in effect come from.
/// </summary>
public enum AttributeSettingsSource
{
    Organization,
    Space,
    Epic,
}

public sealed record AttributeSettingsDto
{
    public required AttributeSettingsSource Source { get; init; }

    /// <summary>
    /// Every attribute of the organization, in the organization's order. In a space or epic with its own settings, an
    /// attribute they don't mention (added to the organization after they were last saved) is off.
    /// </summary>
    public required AttributeSettingDto[] Attributes { get; init; }
}

public sealed record AttributeSettingDto
{
    public required long AttributeId { get; init; }
    public required string Name { get; init; }
    public required string Color { get; init; }
    public required AttributeType Type { get; init; }
    public required bool IsEnabled { get; init; }
    public required bool ShowOnCard { get; init; }
}

public class AttributeSettingsReader(DatabaseContext context) : IAttributeSettingsReader
{
    public async Task<AttributeSettingsDto> GetForSpace(long spaceId, CancellationToken cancellationToken)
    {
        var space = await context.Spaces
            .Where(x => x.Id == spaceId)
            .Select(x => new
            {
                x.OrganizationId,
                HasOwnSettings = context.SpaceAttributeSettings.Any(s => s.SpaceId == x.Id),
            })
            .FirstAsync(cancellationToken);

        return await Get(
            space.OrganizationId,
            space.HasOwnSettings ? AttributeSettingsSource.Space : AttributeSettingsSource.Organization,
            spaceId,
            cancellationToken);
    }

    public async Task<AttributeSettingsDto> GetForEpic(long epicId, CancellationToken cancellationToken)
    {
        var epic = await context.Epics
            .Where(x => x.Id == epicId)
            .Select(x => new
            {
                x.Space!.OrganizationId,
                x.SpaceId,
                EpicOverrides = !x.IsDefault && context.EpicAttributeSettings.Any(s => s.EpicId == x.Id),
                SpaceOverrides = context.SpaceAttributeSettings.Any(s => s.SpaceId == x.SpaceId),
            })
            .FirstAsync(cancellationToken);

        if (epic.EpicOverrides)
        {
            return await Get(epic.OrganizationId, AttributeSettingsSource.Epic, epicId, cancellationToken);
        }

        return await Get(
            epic.OrganizationId,
            epic.SpaceOverrides ? AttributeSettingsSource.Space : AttributeSettingsSource.Organization,
            epic.SpaceId,
            cancellationToken);
    }

    /// <param name="ownerId">The space's id for <see cref="AttributeSettingsSource.Space"/>, the epic's for
    /// <see cref="AttributeSettingsSource.Epic"/>; unused for the organization.</param>
    private async Task<AttributeSettingsDto> Get(
        long organizationId,
        AttributeSettingsSource source,
        long ownerId,
        CancellationToken cancellationToken)
    {
        var defaults = await context.Attributes
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .Select(x => new AttributeSettingDto
            {
                AttributeId = x.Id,
                Name = x.Name,
                Color = x.Color,
                Type = x.AttributeType,
                IsEnabled = x.IsEnabled,
                ShowOnCard = x.ShowOnCard,
            })
            .ToArrayAsync(cancellationToken);

        var overrides = source switch
        {
            AttributeSettingsSource.Space => await context.SpaceAttributeSettings
                .Where(x => x.SpaceId == ownerId)
                .ToDictionaryAsync(
                    x => x.AttributeId,
                    x => new OverriddenSetting(x.IsEnabled, x.ShowOnCard),
                    cancellationToken),
            AttributeSettingsSource.Epic => await context.EpicAttributeSettings
                .Where(x => x.EpicId == ownerId)
                .ToDictionaryAsync(
                    x => x.AttributeId,
                    x => new OverriddenSetting(x.IsEnabled, x.ShowOnCard),
                    cancellationToken),
            _ => null,
        };

        var attributes = overrides is null
            ? defaults
            : defaults
                .Select(x => overrides.TryGetValue(x.AttributeId, out var setting)
                    ? x with { IsEnabled = setting.IsEnabled, ShowOnCard = setting.ShowOnCard }
                    : x with { IsEnabled = false, ShowOnCard = false })
                .ToArray();

        return new AttributeSettingsDto
        {
            Source = source,
            Attributes = attributes,
        };
    }

    private sealed record OverriddenSetting(bool IsEnabled, bool ShowOnCard);
}
