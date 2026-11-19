using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.TelegramServices.Services.GroupChats;

/// <summary>
/// What can be picked one level below a destination - an organization's spaces, a space's epics, an epic's statuses -
/// labelled the same way wherever a destination is picked: in /link and in the "where should this go?" picker. Each
/// caller makes its own buttons from the options.
/// </summary>
/// <remarks>
/// Only what the user can read is listed. Permissions live on organizations and spaces - an epic or a status has
/// none of its own and is readable with its space - so spaces are filtered one by one, and a space's epics or an
/// epic's statuses are listed only when that space is readable. That holds for forged callback data too.
/// </remarks>
public interface IDestinationOptionsReader
{
    /// <param name="userId">Whose read access decides what's listed.</param>
    /// <returns>Empty for a status (nothing is below it), and when the user can't read the space the options are
    /// in.</returns>
    Task<IReadOnlyList<DestinationOption>> GetOptionsBelow(
        LinkDestinationType type,
        long id,
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// A backlog epic has a single status, never picked by name: picking the epic means that status. Null for any
    /// other epic, whose status is picked from its options, and when the user can't read the epic's space.
    /// </summary>
    Task<long?> GetBacklogStatusId(
        long epicId,
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The user can read <paramref name="destination"/>: its space, for a space, epic or status. An organization
    /// alone is readable here - what's below it is filtered space by space.
    /// </summary>
    Task<bool> CanRead(LinkDestination destination, Guid userId, CancellationToken cancellationToken);
}

public class DestinationOptionsReader(DatabaseContext context, IAccessService accessService) : IDestinationOptionsReader
{
    public async Task<IReadOnlyList<DestinationOption>> GetOptionsBelow(
        LinkDestinationType type,
        long id,
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var authData = new OrganizationAuthData { UserId = userId, OrganizationId = organizationId };

        switch (type)
        {
            case LinkDestinationType.Organization:
                return await accessService.GetAvailableSpaces(
                    authData,
                    x => x
                        .OrderBy(y => y.Id)
                        .Select(y => new DestinationOption(LinkDestinationType.Space, y.Id, "🗂️ " + y.Name))
                        .ToListAsyncEF(cancellationToken),
                    includeDeleted: false,
                    cancellationToken: cancellationToken);

            // The backlog comes first.
            case LinkDestinationType.Space:
                if (!await CanReadSpace(authData, id, cancellationToken))
                    return [];

                return await context.ActiveEpics()
                    .Where(x => x.SpaceId == id)
                    .OrderByDescending(x => x.IsDefault)
                    .ThenBy(x => x.Id)
                    .Select(x => new DestinationOption(LinkDestinationType.Epic, x.Id, (x.IsDefault ? "📥 " : "📋 ") + x.Name))
                    .ToListAsyncEF(cancellationToken);

            case LinkDestinationType.Epic:
                if (!await CanReadEpic(authData, id, cancellationToken))
                    return [];

                return await context.ActiveStatuses()
                    .Where(x => x.EpicId == id)
                    .OrderBy(x => x.Id)
                    .Select(x => new DestinationOption(LinkDestinationType.Status, x.Id, "🏷️ " + x.Name))
                    .ToListAsyncEF(cancellationToken);

            default:
                return [];
        }
    }

    public async Task<long?> GetBacklogStatusId(
        long epicId,
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var authData = new OrganizationAuthData { UserId = userId, OrganizationId = organizationId };
        if (!await CanReadEpic(authData, epicId, cancellationToken))
            return null;

        return await context.ActiveStatuses()
            .Where(x => x.EpicId == epicId && x.Epic!.IsDefault)
            .OrderBy(x => x.Id)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsyncEF(cancellationToken);
    }

    public async Task<bool> CanRead(LinkDestination destination, Guid userId, CancellationToken cancellationToken)
    {
        if (destination.SpaceId is not { } spaceId)
            return true;

        var authData = new OrganizationAuthData { UserId = userId, OrganizationId = destination.OrganizationId };

        return await CanReadSpace(authData, spaceId, cancellationToken);
    }

    private async Task<bool> CanReadSpace(OrganizationAuthData authData, long spaceId, CancellationToken cancellationToken)
    {
        var accessLevels = await accessService.GetAccessLevelsBySpaceId(
            authData,
            spaceId,
            includeDeleted: false,
            cancellationToken);

        return accessLevels?.CanRead == true;
    }

    /// <summary>
    /// An epic is readable with its space - <see cref="IAccessService.GetAccessLevelsByEpicId"/> resolves to it.
    /// </summary>
    private async Task<bool> CanReadEpic(OrganizationAuthData authData, long epicId, CancellationToken cancellationToken)
    {
        var accessLevels = await accessService.GetAccessLevelsByEpicId(
            authData,
            epicId,
            includeDeleted: false,
            cancellationToken);

        return accessLevels?.CanRead == true;
    }
}

/// <param name="Type">What the option is: a space, epic or status.</param>
/// <param name="Label">The button text, with its icon.</param>
public sealed record DestinationOption(LinkDestinationType Type, long Id, string Label);
