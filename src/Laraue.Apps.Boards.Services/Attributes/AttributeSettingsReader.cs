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

    /// <summary>
    /// What the space would follow without its own settings: the organization's defaults.
    /// </summary>
    Task<AttributeSettingsDto> GetInheritedForSpace(long spaceId, CancellationToken cancellationToken);

    /// <summary>
    /// What the epic would follow without its own settings: its space's.
    /// </summary>
    Task<AttributeSettingsDto> GetInheritedForEpic(long epicId, CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="GetForEpic"/> for several epics at once - e.g. the epics of the issues on a page. Epics that don't
    /// exist are left out.
    /// </summary>
    Task<Dictionary<long, AttributeSettingsDto>> GetForEpics(
        IReadOnlyCollection<long> epicIds,
        CancellationToken cancellationToken);
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

    public async Task<AttributeSettingsDto> GetInheritedForSpace(long spaceId, CancellationToken cancellationToken)
    {
        var organizationId = await context.Spaces
            .Where(x => x.Id == spaceId)
            .Select(x => x.OrganizationId)
            .FirstAsync(cancellationToken);

        return Combine(
            AttributeSettingsSource.Organization,
            await GetDefaults(organizationId, cancellationToken),
            overrides: null);
    }

    public async Task<AttributeSettingsDto> GetInheritedForEpic(long epicId, CancellationToken cancellationToken)
    {
        var spaceId = await context.Epics
            .Where(x => x.Id == epicId)
            .Select(x => x.SpaceId)
            .FirstAsync(cancellationToken);

        return await GetForSpace(spaceId, cancellationToken);
    }

    public async Task<Dictionary<long, AttributeSettingsDto>> GetForEpics(
        IReadOnlyCollection<long> epicIds,
        CancellationToken cancellationToken)
    {
        var epics = await context.Epics
            .Where(x => epicIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.SpaceId,
                x.Space!.OrganizationId,
                EpicOverrides = !x.IsDefault && context.EpicAttributeSettings.Any(s => s.EpicId == x.Id),
                SpaceOverrides = context.SpaceAttributeSettings.Any(s => s.SpaceId == x.SpaceId),
            })
            .ToArrayAsync(cancellationToken);

        var organizationIds = epics
            .Select(x => x.OrganizationId)
            .Distinct()
            .ToArray();
        var defaultsByOrganization = (await DefaultsQuery(context.Attributes
                    .Where(x => organizationIds.Contains(x.OrganizationId)))
                .ToArrayAsync(cancellationToken))
            .GroupBy(x => x.OrganizationId)
            .ToDictionary(
                x => x.Key,
                x => x
                    .Select(y => y.Setting)
                    .ToArray());

        var overridingEpicIds = epics
            .Where(x => x.EpicOverrides)
            .Select(x => x.Id)
            .ToArray();
        var epicSettings = (await context.EpicAttributeSettings
                .Where(x => overridingEpicIds.Contains(x.EpicId))
                .Select(x => new { OwnerId = x.EpicId, x.AttributeId, x.IsEnabled, x.ShowOnCard })
                .ToArrayAsync(cancellationToken))
            .GroupBy(x => x.OwnerId)
            .ToDictionary(
                x => x.Key,
                x => x.ToDictionary(y => y.AttributeId, y => new OverriddenSetting(y.IsEnabled, y.ShowOnCard)));

        var overridingSpaceIds = epics
            .Where(x => !x.EpicOverrides && x.SpaceOverrides)
            .Select(x => x.SpaceId)
            .Distinct()
            .ToArray();
        var spaceSettings = (await context.SpaceAttributeSettings
                .Where(x => overridingSpaceIds.Contains(x.SpaceId))
                .Select(x => new { OwnerId = x.SpaceId, x.AttributeId, x.IsEnabled, x.ShowOnCard })
                .ToArrayAsync(cancellationToken))
            .GroupBy(x => x.OwnerId)
            .ToDictionary(
                x => x.Key,
                x => x.ToDictionary(y => y.AttributeId, y => new OverriddenSetting(y.IsEnabled, y.ShowOnCard)));

        return epics.ToDictionary(
            x => x.Id,
            x =>
            {
                var defaults = defaultsByOrganization[x.OrganizationId];
                if (x.EpicOverrides)
                {
                    return Combine(AttributeSettingsSource.Epic, defaults, epicSettings[x.Id]);
                }

                return x.SpaceOverrides
                    ? Combine(AttributeSettingsSource.Space, defaults, spaceSettings[x.SpaceId])
                    : Combine(AttributeSettingsSource.Organization, defaults, overrides: null);
            });
    }

    /// <param name="ownerId">The space's id for <see cref="AttributeSettingsSource.Space"/>, the epic's for
    /// <see cref="AttributeSettingsSource.Epic"/>; unused for the organization.</param>
    private async Task<AttributeSettingsDto> Get(
        long organizationId,
        AttributeSettingsSource source,
        long ownerId,
        CancellationToken cancellationToken)
    {
        var defaults = await GetDefaults(organizationId, cancellationToken);

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

        return Combine(source, defaults, overrides);
    }

    private async Task<AttributeSettingDto[]> GetDefaults(long organizationId, CancellationToken cancellationToken)
    {
        return (await DefaultsQuery(context.Attributes
                    .Where(x => x.OrganizationId == organizationId))
                .ToArrayAsync(cancellationToken))
            .Select(x => x.Setting)
            .ToArray();
    }

    private static IQueryable<OrganizationDefault> DefaultsQuery(IQueryable<DataAccess.Models.Attribute> attributes)
    {
        return attributes
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .Select(x => new OrganizationDefault
            {
                OrganizationId = x.OrganizationId,
                Setting = new AttributeSettingDto
                {
                    AttributeId = x.Id,
                    Name = x.Name,
                    Color = x.Color,
                    Type = x.AttributeType,
                    IsEnabled = x.IsEnabled,
                    ShowOnCard = x.ShowOnCard,
                },
            });
    }

    /// <param name="overrides">The space's or epic's own settings by attribute id; null when it follows the
    /// organization.</param>
    private static AttributeSettingsDto Combine(
        AttributeSettingsSource source,
        AttributeSettingDto[] defaults,
        Dictionary<long, OverriddenSetting>? overrides)
    {
        return new AttributeSettingsDto
        {
            Source = source,
            Attributes = overrides is null
                ? defaults
                : defaults
                    .Select(x => overrides.TryGetValue(x.AttributeId, out var setting)
                        ? x with { IsEnabled = setting.IsEnabled, ShowOnCard = setting.ShowOnCard }
                        : x with { IsEnabled = false, ShowOnCard = false })
                    .ToArray(),
        };
    }

    private sealed record OverriddenSetting(bool IsEnabled, bool ShowOnCard);

    private sealed record OrganizationDefault
    {
        public required long OrganizationId { get; init; }

        public required AttributeSettingDto Setting { get; init; }
    }
}
