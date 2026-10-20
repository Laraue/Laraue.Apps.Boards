using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.WebApiServices;

/// <summary>
/// What a space is about (BRD-312): its description and its links to things outside Boards. Reading needs read access
/// to the space; changing them needs <c>CanUpdateSpace</c>. A space the caller can't read is a 404, like a missing one.
/// </summary>
public interface ISpaceAboutService
{
    Task<SpaceAboutDto> Get(
        string spaceKey,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);

    Task UpdateDescription(
        string spaceKey,
        OrganizationAuthData authData,
        UpdateSpaceDescriptionRequest request,
        CancellationToken cancellationToken);

    Task<SpaceLinkDto> CreateLink(
        string spaceKey,
        OrganizationAuthData authData,
        SpaceLinkRequest request,
        CancellationToken cancellationToken);

    Task UpdateLink(
        string spaceKey,
        long linkId,
        OrganizationAuthData authData,
        SpaceLinkRequest request,
        CancellationToken cancellationToken);

    Task DeleteLink(
        string spaceKey,
        long linkId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
}

public class SpaceAboutService(
    DatabaseContext context,
    ICoreSpacesService coreSpacesService,
    ICoreSpaceLinksService coreSpaceLinksService,
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

    public async Task UpdateDescription(
        string spaceKey,
        OrganizationAuthData authData,
        UpdateSpaceDescriptionRequest request,
        CancellationToken cancellationToken)
    {
        var spaceId = await GetEditableSpaceId(spaceKey, authData, cancellationToken);
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        await coreSpacesService.Update(
            spaceId,
            setters => setters.SetProperty(x => x.Description, description),
            cancellationToken);
    }

    public async Task<SpaceLinkDto> CreateLink(
        string spaceKey,
        OrganizationAuthData authData,
        SpaceLinkRequest request,
        CancellationToken cancellationToken)
    {
        var spaceId = await GetEditableSpaceId(spaceKey, authData, cancellationToken);
        var url = request.Url.Trim();
        var description = request.Description.Trim();

        var id = await coreSpaceLinksService.Create(
            spaceId,
            url,
            description,
            authData.UserId,
            cancellationToken);

        return new SpaceLinkDto
        {
            Id = id,
            Url = url,
            Description = description,
        };
    }

    public async Task UpdateLink(
        string spaceKey,
        long linkId,
        OrganizationAuthData authData,
        SpaceLinkRequest request,
        CancellationToken cancellationToken)
    {
        var spaceId = await GetEditableSpaceId(spaceKey, authData, cancellationToken);

        var updated = await coreSpaceLinksService.Update(
            spaceId,
            linkId,
            request.Url.Trim(),
            request.Description.Trim(),
            cancellationToken);

        if (!updated)
            throw LinkNotFound(linkId);
    }

    public async Task DeleteLink(
        string spaceKey,
        long linkId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var spaceId = await GetEditableSpaceId(spaceKey, authData, cancellationToken);

        var deleted = await coreSpaceLinksService.Delete(
            spaceId,
            linkId,
            cancellationToken);

        if (!deleted)
            throw LinkNotFound(linkId);
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

    /// <summary>The space's id when the caller may change it: 404 when it can't be read, 403 when it can't be updated.</summary>
    private async Task<long> GetEditableSpaceId(
        string spaceKey,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var space = await GetReadableSpace(spaceKey, authData, cancellationToken);
        if (!space.AccessLevels.CanUpdateSpace)
        {
            throw new ForbiddenException(string.Format(ErrorMessages.EntityActionForbidden, "Space", spaceKey, "update"));
        }

        return space.Id;
    }

    private static NotFoundException LinkNotFound(long linkId)
    {
        return new NotFoundException(string.Format(ErrorMessages.EntityNotFound, "SpaceLink", linkId));
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

public record UpdateSpaceDescriptionRequest
{
    /// <summary>Markdown; empty or null clears it.</summary>
    [MaxLength(Constraints.MaxSpaceDescriptionLength)]
    public string? Description { get; init; }
}

public record SpaceLinkRequest
{
    [Required]
    [Url]
    [MaxLength(Constraints.MaxSpaceLinkUrlLength)]
    public required string Url { get; init; }

    [Required]
    [MinLength(1)]
    [MaxLength(Constraints.MaxSpaceLinkDescriptionLength)]
    public required string Description { get; init; }
}
