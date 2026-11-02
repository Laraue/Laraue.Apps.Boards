using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.Resources;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.Attributes;

/// <summary>
/// A space's or an epic's own attribute settings (BRD-179). Overriding copies the settings the space or epic follows
/// at that moment; from then on changes above it don't reach it, until it's reset. The order of attributes is the
/// organization's everywhere, so it's not part of these settings. No permission checks, like every
/// core service: the caller checks that the person may update the space or epic. Every method needs a transaction
/// opened by the caller.
/// </summary>
public interface ICoreAttributeSettingsService
{
    /// <summary>
    /// Gives the space its own settings, copied from the organization's defaults. Does nothing when it already has them.
    /// </summary>
    Task OverrideSpace(long spaceId, CancellationToken cancellationToken);

    /// <summary>
    /// Gives the epic its own settings, copied from what it follows now - its space's or the organization's. Does
    /// nothing when it already has them; <see cref="BadRequestException"/> for the backlog, which always follows its
    /// space.
    /// </summary>
    Task OverrideEpic(long epicId, CancellationToken cancellationToken);

    /// <summary>
    /// Makes <paramref name="settings"/> the space's settings: attributes left out are off there and shown as new.
    /// <see cref="BadRequestException"/>
    /// when the space doesn't override the settings, or an attribute isn't the organization's or is listed twice.
    /// </summary>
    Task UpdateSpace(
        long spaceId,
        IReadOnlyList<AttributeSettingChange> settings,
        CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="UpdateSpace"/> for an epic.
    /// </summary>
    Task UpdateEpic(
        long epicId,
        IReadOnlyList<AttributeSettingChange> settings,
        CancellationToken cancellationToken);

    /// <summary>
    /// Drops the space's own settings: it follows the organization's defaults again. Epics with their own settings
    /// keep them.
    /// </summary>
    Task ResetSpace(long spaceId, CancellationToken cancellationToken);

    /// <summary>
    /// Drops the epic's own settings: it follows its space again.
    /// </summary>
    Task ResetEpic(long epicId, CancellationToken cancellationToken);
}

public sealed record AttributeSettingChange(long AttributeId, bool IsEnabled, bool ShowOnCard);

public class CoreAttributeSettingsService(
    DatabaseContext context,
    IAttributeSettingsReader attributeSettingsReader)
    : ICoreAttributeSettingsService
{
    public async Task OverrideSpace(long spaceId, CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var current = await attributeSettingsReader.GetForSpace(spaceId, cancellationToken);
        if (current.Source == AttributeSettingsSource.Space)
        {
            return;
        }

        context.SpaceAttributeSettings.AddRange(current.Attributes
            .Select(x => new SpaceAttributeSetting
            {
                SpaceId = spaceId,
                AttributeId = x.AttributeId,
                IsEnabled = x.IsEnabled,
                ShowOnCard = x.ShowOnCard,
            }));
        await context.SaveChangesAsync(cancellationToken);

        await SetSpaceOverride(spaceId, true, cancellationToken);
    }

    public async Task OverrideEpic(long epicId, CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();
        await EnsureNotDefault(epicId, cancellationToken);

        var current = await attributeSettingsReader.GetForEpic(epicId, cancellationToken);
        if (current.Source == AttributeSettingsSource.Epic)
        {
            return;
        }

        // Attributes new to the space's settings stay without a row: still new to the epic's.
        context.EpicAttributeSettings.AddRange(current.Attributes
            .Where(x => !x.IsNew)
            .Select(x => new EpicAttributeSetting
            {
                EpicId = epicId,
                AttributeId = x.AttributeId,
                IsEnabled = x.IsEnabled,
                ShowOnCard = x.ShowOnCard,
            }));
        await context.SaveChangesAsync(cancellationToken);

        await SetEpicOverride(epicId, true, cancellationToken);
    }

    public async Task UpdateSpace(
        long spaceId,
        IReadOnlyList<AttributeSettingChange> settings,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var space = await context.Spaces
            .Where(x => x.Id == spaceId)
            .Select(x => new { x.OrganizationId, x.HasAttributeOverride })
            .FirstAsync(cancellationToken);
        if (!space.HasAttributeOverride)
        {
            throw new BadRequestException(nameof(spaceId), ErrorMessages.AttributeSettingsNotOverridden);
        }

        await Validate(space.OrganizationId, settings, cancellationToken);

        await context.SpaceAttributeSettings
            .Where(x => x.SpaceId == spaceId)
            .ExecuteDeleteAsync(cancellationToken);
        context.SpaceAttributeSettings.AddRange(settings
            .Select(x => new SpaceAttributeSetting
            {
                SpaceId = spaceId,
                AttributeId = x.AttributeId,
                IsEnabled = x.IsEnabled,
                ShowOnCard = x.ShowOnCard,
            }));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateEpic(
        long epicId,
        IReadOnlyList<AttributeSettingChange> settings,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var epic = await context.Epics
            .Where(x => x.Id == epicId)
            .Select(x => new { x.Space!.OrganizationId, x.HasAttributeOverride })
            .FirstAsync(cancellationToken);
        if (!epic.HasAttributeOverride)
        {
            throw new BadRequestException(nameof(epicId), ErrorMessages.AttributeSettingsNotOverridden);
        }

        await Validate(epic.OrganizationId, settings, cancellationToken);

        await context.EpicAttributeSettings
            .Where(x => x.EpicId == epicId)
            .ExecuteDeleteAsync(cancellationToken);
        context.EpicAttributeSettings.AddRange(settings
            .Select(x => new EpicAttributeSetting
            {
                EpicId = epicId,
                AttributeId = x.AttributeId,
                IsEnabled = x.IsEnabled,
                ShowOnCard = x.ShowOnCard,
            }));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetSpace(long spaceId, CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        await context.SpaceAttributeSettings
            .Where(x => x.SpaceId == spaceId)
            .ExecuteDeleteAsync(cancellationToken);
        await SetSpaceOverride(spaceId, false, cancellationToken);
    }

    public async Task ResetEpic(long epicId, CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        await context.EpicAttributeSettings
            .Where(x => x.EpicId == epicId)
            .ExecuteDeleteAsync(cancellationToken);
        await SetEpicOverride(epicId, false, cancellationToken);
    }

    private async Task EnsureNotDefault(long epicId, CancellationToken cancellationToken)
    {
        var isDefault = await context.Epics
            .Where(x => x.Id == epicId)
            .Select(x => x.IsDefault)
            .FirstAsync(cancellationToken);
        if (isDefault)
        {
            throw new BadRequestException(nameof(epicId), ErrorMessages.BacklogAttributeSettingsFollowSpace);
        }
    }

    private async Task Validate(
        long organizationId,
        IReadOnlyList<AttributeSettingChange> settings,
        CancellationToken cancellationToken)
    {
        var attributeIds = settings
            .Select(x => x.AttributeId)
            .ToArray();
        var organizationAttributeIds = await context.Attributes
            .Where(x => x.OrganizationId == organizationId && attributeIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSetAsync(cancellationToken);

        var errors = new Dictionary<string, string?[]>();
        var seen = new HashSet<long>();
        for (var i = 0; i < settings.Count; i++)
        {
            var attributeId = settings[i].AttributeId;
            if (!organizationAttributeIds.Contains(attributeId))
            {
                errors[$"{nameof(settings)}[{i}].attributeId"] = [ErrorMessages.AttributeNotFound];
            }
            else if (!seen.Add(attributeId))
            {
                errors[$"{nameof(settings)}[{i}].attributeId"] = [ErrorMessages.AttributeSettingDuplicated];
            }
        }

        if (errors.Count > 0)
        {
            throw new BadRequestException(errors);
        }
    }

    private Task SetSpaceOverride(long spaceId, bool value, CancellationToken cancellationToken)
    {
        return context.Spaces
            .Where(x => x.Id == spaceId)
            .ExecuteUpdateAsync(
                x => x.SetProperty(p => p.HasAttributeOverride, value),
                cancellationToken);
    }

    private Task SetEpicOverride(long epicId, bool value, CancellationToken cancellationToken)
    {
        return context.Epics
            .Where(x => x.Id == epicId)
            .ExecuteUpdateAsync(
                x => x.SetProperty(p => p.HasAttributeOverride, value),
                cancellationToken);
    }
}
