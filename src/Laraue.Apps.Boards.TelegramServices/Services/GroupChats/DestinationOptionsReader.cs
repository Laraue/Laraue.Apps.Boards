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
public interface IDestinationOptionsReader
{
    /// <param name="userId">Spaces are those this user can see in the organization.</param>
    /// <returns>Empty for a status: nothing is below it.</returns>
    Task<IReadOnlyList<DestinationOption>> GetOptionsBelow(
        LinkDestinationType type,
        long id,
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// A backlog epic has a single status, never picked by name: picking the epic means that status. Null for any
    /// other epic, whose status is picked from its options.
    /// </summary>
    Task<long?> GetBacklogStatusId(long epicId, CancellationToken cancellationToken);
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
        switch (type)
        {
            case LinkDestinationType.Organization:
                return await accessService.GetAvailableSpaces(
                    new OrganizationAuthData { UserId = userId, OrganizationId = organizationId },
                    x => x
                        .OrderBy(y => y.Id)
                        .Select(y => new DestinationOption(LinkDestinationType.Space, y.Id, "🗂️ " + y.Name))
                        .ToListAsyncEF(cancellationToken),
                    includeDeleted: false,
                    cancellationToken: cancellationToken);

            // The backlog comes first.
            case LinkDestinationType.Space:
                return await context.ActiveEpics()
                    .Where(x => x.SpaceId == id)
                    .OrderByDescending(x => x.IsDefault)
                    .ThenBy(x => x.Id)
                    .Select(x => new DestinationOption(LinkDestinationType.Epic, x.Id, (x.IsDefault ? "📥 " : "📋 ") + x.Name))
                    .ToListAsyncEF(cancellationToken);

            case LinkDestinationType.Epic:
                return await context.ActiveStatuses()
                    .Where(x => x.EpicId == id)
                    .OrderBy(x => x.Id)
                    .Select(x => new DestinationOption(LinkDestinationType.Status, x.Id, "🏷️ " + x.Name))
                    .ToListAsyncEF(cancellationToken);

            default:
                return [];
        }
    }

    public Task<long?> GetBacklogStatusId(long epicId, CancellationToken cancellationToken)
    {
        return context.ActiveStatuses()
            .Where(x => x.EpicId == epicId && x.Epic!.IsDefault)
            .OrderBy(x => x.Id)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsyncEF(cancellationToken);
    }
}

/// <param name="Type">What the option is: a space, epic or status.</param>
/// <param name="Label">The button text, with its icon.</param>
public sealed record DestinationOption(LinkDestinationType Type, long Id, string Label);
