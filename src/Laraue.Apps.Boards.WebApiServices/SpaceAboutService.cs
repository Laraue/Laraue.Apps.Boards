using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.WebApiServices;

/// <summary>
/// What a space is about (BRD-312): its description and its links to things outside Boards, for the space page. Reading
/// needs read access to the space - a space the caller can't read is a 404, like a missing one. They're changed with the
/// space's other settings (<c>PUT /api/spaces/{key}</c>).
/// </summary>
public interface ISpaceAboutService
{
    Task<SpaceAboutDto> Get(
        string spaceKey,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
}

public class SpaceAboutService(
    DatabaseContext context,
    ICoreSpacesService coreSpacesService,
    IAccessService accessService)
    : ISpaceAboutService
{
    public async Task<SpaceAboutDto> Get(
        string spaceKey,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var space = await GetReadableSpace(spaceKey, authData, cancellationToken);

        var description = await context.Spaces
            .Where(x => x.Id == space.Id)
            .Select(x => x.Description)
            .FirstAsync(cancellationToken);

        var links = await context.SpaceLinks
            .Where(x => x.SpaceId == space.Id)
            .OrderBy(x => x.Id)
            .Select(x => new SpaceLinkDto
            {
                Id = x.Id,
                Url = x.Url,
                Description = x.Description,
            })
            .ToArrayAsync(cancellationToken);

        return new SpaceAboutDto
        {
            Description = description,
            Links = links,
            CanEdit = space.AccessLevels.CanUpdateSpace,
        };
    }

    /// <summary>The space by key with the caller's access to it; 404 when it doesn't exist or can't be read.</summary>
    private async Task<ReadableSpace> GetReadableSpace(
        string spaceKey,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(
            authData.OrganizationId,
            spaceKey,
            cancellationToken);

        var accessLevels = await accessService
            .GetAccessLevelsBySpaceId(authData, spaceId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Space", spaceKey))
            .EnsureOrThrowNotFound(
                a => a.CanRead,
                string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Space", spaceKey));

        return new ReadableSpace(spaceId, accessLevels);
    }

    private sealed record ReadableSpace(long Id, AccessLevels AccessLevels);
}

public record SpaceAboutDto
{
    /// <summary>Markdown, or null when the space has none.</summary>
    public string? Description { get; init; }

    public required SpaceLinkDto[] Links { get; init; }

    /// <summary>Whether the caller may change the description and links.</summary>
    public required bool CanEdit { get; init; }
}

public record SpaceLinkDto
{
    public required long Id { get; init; }

    public required string Url { get; init; }

    /// <summary>Markdown: what the link is and when it matters.</summary>
    public required string Description { get; init; }
}
