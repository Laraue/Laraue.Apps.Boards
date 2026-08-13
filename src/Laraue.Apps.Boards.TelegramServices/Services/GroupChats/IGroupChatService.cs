using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.TelegramServices.Services.GroupChats;

public record OrganizationOption(long Id, string Name);

public record SpaceOption(long Id, string Name);

public record EpicOption(long Id, string Name);

public record StatusOption(long Id, string Name);

public record LinkedChatDestination(
    string OrganizationName,
    string SpaceName,
    string? EpicName = null,
    string? StatusName = null)
{
    /// <summary>
    /// Human-readable destination path, e.g. "Acme Corp / Backend" or, when narrowed,
    /// "Acme Corp / Backend / Sprint 4 / In Progress".
    /// </summary>
    public string BuildPath()
    {
        var parts = new List<string> { OrganizationName, SpaceName };

        if (EpicName is not null)
            parts.Add(EpicName);

        if (StatusName is not null)
            parts.Add(StatusName);

        return string.Join(" / ", parts);
    }
}

public interface IGroupChatService
{
    /// <summary>
    /// Organizations a user is allowed to link a chat to: their own Personal
    /// organization plus any Organization-type org where they have the
    /// <see cref="OrganizationUser.CanLinkChat"/> permission.
    /// </summary>
    Task<List<OrganizationOption>> GetLinkableOrganizations(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Re-checks that a user is still allowed to link a chat to the given organization.
    /// </summary>
    Task<bool> CanLinkToOrganization(Guid userId, long organizationId, CancellationToken cancellationToken);

    Task<List<SpaceOption>> GetSpaces(long organizationId, CancellationToken cancellationToken);

    Task<string?> GetOrganizationName(long organizationId, CancellationToken cancellationToken);

    Task<string?> GetSpaceName(long spaceId, CancellationToken cancellationToken);

    Task<List<EpicOption>> GetEpics(long spaceId, CancellationToken cancellationToken);

    Task<List<StatusOption>> GetStatuses(long epicId, CancellationToken cancellationToken);

    Task<string?> GetEpicName(long epicId, CancellationToken cancellationToken);

    Task<LinkedTelegramChat?> GetLink(long externalChatId, CancellationToken cancellationToken);

    /// <summary>
    /// Links (or re-links) a chat to a space, at "backlog" level (no epic/status narrowing).
    /// </summary>
    Task<LinkedChatDestination> LinkToSpace(
        long externalChatId,
        string? chatTitle,
        long organizationId,
        long spaceId,
        Guid linkedByUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Links (or re-links) a chat to a specific epic/status within a space.
    /// </summary>
    Task<LinkedChatDestination> LinkToStatus(
        long externalChatId,
        string? chatTitle,
        long organizationId,
        long spaceId,
        long epicId,
        long statusId,
        Guid linkedByUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Clears a chat's link (organization/space/epic/status), if any.
    /// </summary>
    Task Unlink(long externalChatId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the status a card created from this link should land in: the link's own
    /// <see cref="LinkedTelegramChat.StatusId"/> if narrowed that far, otherwise the
    /// <see cref="LinkedTelegramChat.EpicId"/>'s (or the space's default epic's) first status.
    /// </summary>
    Task<long> GetDestinationStatusId(LinkedTelegramChat link, CancellationToken cancellationToken);
}

public class GroupChatService(
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider)
    : IGroupChatService
{
    public Task<List<OrganizationOption>> GetLinkableOrganizations(Guid userId, CancellationToken cancellationToken)
    {
        var personalOrgs = context.Organizations
            .Where(o => o.Type == OrganizationType.Personal && o.OwnerId == userId);

        var memberOrgs = context.Organizations
            .Where(o => o.Type == OrganizationType.Organization)
            .Where(o => o.Users!.Any(u => u.UserId == userId && u.CanLinkChat));

        return personalOrgs
            .Concat(memberOrgs)
            .OrderBy(o => o.Name)
            .Select(o => new OrganizationOption(o.Id, o.Name))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> CanLinkToOrganization(Guid userId, long organizationId, CancellationToken cancellationToken)
    {
        return context.Organizations
            .Where(o => o.Id == organizationId)
            .Where(o =>
                (o.Type == OrganizationType.Personal && o.OwnerId == userId)
                || (o.Type == OrganizationType.Organization
                    && o.Users!.Any(u => u.UserId == userId && u.CanLinkChat)))
            .AnyAsync(cancellationToken);
    }

    public Task<List<SpaceOption>> GetSpaces(long organizationId, CancellationToken cancellationToken)
    {
        return context.Spaces
            .Where(s => s.OrganizationId == organizationId)
            .OrderBy(s => s.Name)
            .Select(s => new SpaceOption(s.Id, s.Name))
            .ToListAsync(cancellationToken);
    }

    public Task<string?> GetOrganizationName(long organizationId, CancellationToken cancellationToken)
    {
        return context.Organizations
            .Where(o => o.Id == organizationId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<string?> GetSpaceName(long spaceId, CancellationToken cancellationToken)
    {
        return context.Spaces
            .Where(s => s.Id == spaceId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<List<EpicOption>> GetEpics(long spaceId, CancellationToken cancellationToken)
    {
        return context.Epics
            .Where(e => e.SpaceId == spaceId)
            .OrderBy(e => e.Name)
            .Select(e => new EpicOption(e.Id, e.Name))
            .ToListAsync(cancellationToken);
    }

    public Task<List<StatusOption>> GetStatuses(long epicId, CancellationToken cancellationToken)
    {
        return context.Statuses
            .Where(s => s.EpicId == epicId)
            .OrderBy(s => s.SortOrder)
            .Select(s => new StatusOption(s.Id, s.Name))
            .ToListAsync(cancellationToken);
    }

    public Task<string?> GetEpicName(long epicId, CancellationToken cancellationToken)
    {
        return context.Epics
            .Where(e => e.Id == epicId)
            .Select(e => e.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<LinkedTelegramChat?> GetLink(long externalChatId, CancellationToken cancellationToken)
    {
        return context.LinkedTelegramChats
            .Include(x => x.Organization)
            .Include(x => x.Space)
            .Include(x => x.Epic)
            .Include(x => x.Status)
            .FirstOrDefaultAsync(x => x.ExternalChatId == externalChatId, cancellationToken);
    }

    public async Task<LinkedChatDestination> LinkToSpace(
        long externalChatId,
        string? chatTitle,
        long organizationId,
        long spaceId,
        Guid linkedByUserId,
        CancellationToken cancellationToken)
    {
        var spaceData = await context.Spaces
            .Where(s => s.Id == spaceId)
            .Select(s => new { SpaceName = s.Name, OrganizationName = s.Organization!.Name })
            .FirstOrThrowNotFoundEFAsync($"Space {spaceId} not found", cancellationToken);

        var chat = await context.LinkedTelegramChats
            .FirstOrDefaultAsync(x => x.ExternalChatId == externalChatId, cancellationToken);

        if (chat is null)
        {
            chat = new LinkedTelegramChat { ExternalChatId = externalChatId };
            context.Add(chat);
        }

        chat.Title = chatTitle;
        chat.OrganizationId = organizationId;
        chat.SpaceId = spaceId;
        chat.EpicId = null;
        chat.StatusId = null;
        chat.LinkedByUserId = linkedByUserId;
        chat.LinkedAt = dateTimeProvider.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return new LinkedChatDestination(spaceData.OrganizationName, spaceData.SpaceName);
    }

    public async Task<LinkedChatDestination> LinkToStatus(
        long externalChatId,
        string? chatTitle,
        long organizationId,
        long spaceId,
        long epicId,
        long statusId,
        Guid linkedByUserId,
        CancellationToken cancellationToken)
    {
        var statusData = await context.Statuses
            .Where(s => s.Id == statusId)
            .Select(s => new
            {
                StatusName = s.Name,
                EpicName = s.Epic!.Name,
                SpaceName = s.Epic.Space!.Name,
                OrganizationName = s.Epic.Space.Organization!.Name,
            })
            .FirstOrThrowNotFoundEFAsync($"Status {statusId} not found", cancellationToken);

        var chat = await context.LinkedTelegramChats
            .FirstOrDefaultAsync(x => x.ExternalChatId == externalChatId, cancellationToken);

        if (chat is null)
        {
            chat = new LinkedTelegramChat { ExternalChatId = externalChatId };
            context.Add(chat);
        }

        chat.Title = chatTitle;
        chat.OrganizationId = organizationId;
        chat.SpaceId = spaceId;
        chat.EpicId = epicId;
        chat.StatusId = statusId;
        chat.LinkedByUserId = linkedByUserId;
        chat.LinkedAt = dateTimeProvider.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return new LinkedChatDestination(
            statusData.OrganizationName,
            statusData.SpaceName,
            statusData.EpicName,
            statusData.StatusName);
    }

    public Task Unlink(long externalChatId, CancellationToken cancellationToken)
    {
        return context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == externalChatId)
            .ExecuteUpdateAsync(upd => upd
                    .SetProperty(x => x.OrganizationId, (long?)null)
                    .SetProperty(x => x.SpaceId, (long?)null)
                    .SetProperty(x => x.EpicId, (long?)null)
                    .SetProperty(x => x.StatusId, (long?)null),
                cancellationToken);
    }

    public async Task<long> GetDestinationStatusId(LinkedTelegramChat link, CancellationToken cancellationToken)
    {
        if (link.StatusId is not null)
            return link.StatusId.Value;

        var statusQuery = context.Statuses.Where(s => s.Epic!.SpaceId == link.SpaceId);

        statusQuery = link.EpicId is not null
            ? statusQuery.Where(s => s.EpicId == link.EpicId)
            : statusQuery.Where(s => s.Epic!.IsDefault);

        var statusData = await statusQuery
            .OrderBy(s => s.SortOrder)
            .Select(s => new { s.Id })
            .FirstOrThrowNotFoundEFAsync("Status to save TG message is not defined", cancellationToken);

        return statusData.Id;
    }
}
