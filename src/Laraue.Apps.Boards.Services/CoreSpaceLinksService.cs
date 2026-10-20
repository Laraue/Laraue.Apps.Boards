using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.Resources;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// A space's links to things outside Boards (<see cref="SpaceLink"/>, BRD-312): add, change, remove. A URL is listed
/// once per space - a second one is detected by the unique index itself - and a space keeps at most
/// <see cref="Constraints.MaxSpaceLinksPerSpace"/>. No permission checks, like every core service: the caller checks
/// that the person may update the space.
/// </summary>
public interface ICoreSpaceLinksService
{
    /// <summary>
    /// Adds a link and returns its id. <see cref="BadRequestException"/> when the URL is already a link of the space or
    /// the space has the maximum number of links.
    /// </summary>
    Task<long> Create(
        long spaceId,
        string url,
        string description,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Changes a link of the space. False when the space has no link with this id; <see cref="BadRequestException"/>
    /// when the new URL is already another link of the space.
    /// </summary>
    Task<bool> Update(
        long spaceId,
        long linkId,
        string url,
        string description,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes a link of the space. False when the space has no link with this id.
    /// </summary>
    Task<bool> Delete(
        long spaceId,
        long linkId,
        CancellationToken cancellationToken);
}

public class CoreSpaceLinksService(
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider)
    : ICoreSpaceLinksService
{
    public async Task<long> Create(
        long spaceId,
        string url,
        string description,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var count = await context.SpaceLinks.CountAsync(x => x.SpaceId == spaceId, cancellationToken);
        if (count >= Constraints.MaxSpaceLinksPerSpace)
        {
            throw new BadRequestException(
                "url",
                string.Format(ErrorMessages.SpaceLinksLimitReached, Constraints.MaxSpaceLinksPerSpace));
        }

        var link = new SpaceLink
        {
            SpaceId = spaceId,
            Url = url,
            Description = description,
            CreatedAt = dateTimeProvider.UtcNow,
            CreatedByUserId = userId,
        };

        context.SpaceLinks.Add(link);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUrlViolation(ex))
        {
            context.Entry(link).State = EntityState.Detached;
            throw new BadRequestException("url", ErrorMessages.SpaceLinkUrlAlreadyAdded);
        }

        return link.Id;
    }

    public async Task<bool> Update(
        long spaceId,
        long linkId,
        string url,
        string description,
        CancellationToken cancellationToken)
    {
        try
        {
            var updatedCount = await context.SpaceLinks
                .Where(x => x.Id == linkId && x.SpaceId == spaceId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.Url, url)
                        .SetProperty(x => x.Description, description),
                    cancellationToken);

            return updatedCount > 0;
        }
        catch (PostgresException ex) when (IsUrlViolation(ex))
        {
            throw new BadRequestException("url", ErrorMessages.SpaceLinkUrlAlreadyAdded);
        }
    }

    public async Task<bool> Delete(
        long spaceId,
        long linkId,
        CancellationToken cancellationToken)
    {
        var deletedCount = await context.SpaceLinks
            .Where(x => x.Id == linkId && x.SpaceId == spaceId)
            .ExecuteDeleteAsync(cancellationToken);

        return deletedCount > 0;
    }

    private static bool IsUrlViolation(Exception ex)
    {
        var pgException = ex as PostgresException ?? ex.InnerException as PostgresException;

        return pgException is
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: Constraints.SpaceLinkUrlIndexName,
        };
    }
}
