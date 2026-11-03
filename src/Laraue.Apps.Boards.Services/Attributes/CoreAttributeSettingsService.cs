using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.Resources;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.Attributes;

/// <summary>
/// A space's or an epic's own attribute settings (BRD-179), saved with the rest of its settings. Once it has its own,
/// changes above it don't reach it, until they're dropped. The order of attributes is the organization's everywhere,
/// so it's not part of these settings. No permission checks, like every core service: the caller checks that the
/// person may update the space or epic. Every method needs a transaction opened by the caller.
/// </summary>
public interface ICoreAttributeSettingsService
{
    /// <summary>
    /// Saves the space's attribute settings: null drops its own, so it follows the organization's defaults again
    /// (epics with their own settings keep theirs); a list becomes its own settings, every attribute of the
    /// organization saved and those left out off. <see cref="BadRequestException"/> when an attribute isn't the
    /// organization's or is listed twice.
    /// </summary>
    Task SaveSpace(
        long spaceId,
        IReadOnlyList<AttributeSettingChange>? settings,
        CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="SaveSpace"/> for an epic: null makes it follow its space again. <see cref="BadRequestException"/>
    /// for own settings of the backlog, which always follows its space.
    /// </summary>
    Task SaveEpic(
        long epicId,
        IReadOnlyList<AttributeSettingChange>? settings,
        CancellationToken cancellationToken);
}

public sealed record AttributeSettingChange(long AttributeId, bool IsEnabled, bool ShowOnCard);

public class CoreAttributeSettingsService(DatabaseContext context) : ICoreAttributeSettingsService
{
    public async Task SaveSpace(
        long spaceId,
        IReadOnlyList<AttributeSettingChange>? settings,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        await context.SpaceAttributeSettings
            .Where(x => x.SpaceId == spaceId)
            .ExecuteDeleteAsync(cancellationToken);
        if (settings is null)
            return;

        var organizationId = await context.Spaces
            .Where(x => x.Id == spaceId)
            .Select(x => x.OrganizationId)
            .FirstAsync(cancellationToken);
        var all = await Complete(organizationId, settings, cancellationToken);

        context.SpaceAttributeSettings.AddRange(all
            .Select(x => new SpaceAttributeSetting
            {
                SpaceId = spaceId,
                AttributeId = x.AttributeId,
                IsEnabled = x.IsEnabled,
                ShowOnCard = x.ShowOnCard,
            }));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveEpic(
        long epicId,
        IReadOnlyList<AttributeSettingChange>? settings,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        await context.EpicAttributeSettings
            .Where(x => x.EpicId == epicId)
            .ExecuteDeleteAsync(cancellationToken);
        if (settings is null)
            return;

        var epic = await context.Epics
            .Where(x => x.Id == epicId)
            .Select(x => new { x.Space!.OrganizationId, x.IsDefault })
            .FirstAsync(cancellationToken);
        if (epic.IsDefault)
        {
            throw new BadRequestException(nameof(epicId), ErrorMessages.BacklogAttributeSettingsFollowSpace);
        }

        var all = await Complete(epic.OrganizationId, settings, cancellationToken);

        context.EpicAttributeSettings.AddRange(all
            .Select(x => new EpicAttributeSetting
            {
                EpicId = epicId,
                AttributeId = x.AttributeId,
                IsEnabled = x.IsEnabled,
                ShowOnCard = x.ShowOnCard,
            }));
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Validates <paramref name="settings"/> and adds every other attribute of the organization as off - so the saved
    /// settings mention all of them, and a space or epic with its own settings always has rows while the organization
    /// has attributes.
    /// </summary>
    private async Task<AttributeSettingChange[]> Complete(
        long organizationId,
        IReadOnlyList<AttributeSettingChange> settings,
        CancellationToken cancellationToken)
    {
        var organizationAttributeIds = await context.Attributes
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);
        var known = organizationAttributeIds.ToHashSet();

        var errors = new Dictionary<string, string?[]>();
        var seen = new HashSet<long>();
        for (var i = 0; i < settings.Count; i++)
        {
            var attributeId = settings[i].AttributeId;
            if (!known.Contains(attributeId))
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

        return settings
            .Concat(organizationAttributeIds
                .Where(x => !seen.Contains(x))
                .Select(x => new AttributeSettingChange(x, IsEnabled: false, ShowOnCard: false)))
            .ToArray();
    }
}
