using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Boards.WebApiServices;

/// <summary>
/// The organization setup window (BRD-293): name the space sign-up created, then add a sample project or not.
/// </summary>
public interface IOrganizationSetupService
{
    Task Complete(
        CompleteOrganizationSetupRequest request,
        CancellationToken cancellationToken);
}

public class OrganizationSetupService(
    ICoreOrganizationSetupService coreOrganizationSetupService,
    ICoreSpacesService coreSpacesService,
    IAccessService accessService,
    DatabaseContext context)
    : IOrganizationSetupService
{
    public async Task Complete(
        CompleteOrganizationSetupRequest request,
        CancellationToken cancellationToken)
    {
        var canUpdateOrganization = await accessService.HasAccess(
            request.AuthData,
            AdminAccessLevel.UpdateOrganization,
            cancellationToken);

        if (!canUpdateOrganization)
            throw new NotFoundException(string.Format(
                ErrorMessages.AdminAccessRequired,
                request.AuthData.OrganizationId,
                "Organization setup",
                AdminAccessLevel.UpdateOrganization));

        // The space is renamed only if the setup is completed, and the setup only if the rename succeeds.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        if (request.Space is { } space)
            await RenameSpace(request.AuthData, space, cancellationToken);

        var isCompleted = await coreOrganizationSetupService.Complete(
            request.AuthData.OrganizationId,
            request.AuthData.UserId,
            request.Template!.Value,
            cancellationToken);

        if (!isCompleted)
            throw new BadRequestException(nameof(request.Template), ErrorMessages.OrganizationAlreadySetUp);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RenameSpace(
        OrganizationAuthData authData,
        OrganizationSetupSpace space,
        CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(
            authData.OrganizationId,
            space.Key,
            cancellationToken);

        await accessService.GetAccessLevelsBySpaceId(authData, spaceId, includeDeleted: false, cancellationToken: cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Space", space.Key))
            .EnsureOrThrowForbidden(a => a.CanUpdateSpace, string.Format(ErrorMessages.EntityNotAccessible, "Space", space.Key));

        await coreSpacesService.Update(
            spaceId,
            setters => setters
                .SetProperty(x => x.Name, space.Name.Trim())
                .SetProperty(x => x.Key, space.NewKey.ToUpper()),
            cancellationToken);
    }
}

public record CompleteOrganizationSetupRequest
{
    public OrganizationAuthData AuthData { get; set; }

    /// <summary>
    /// The new name and key of the space sign-up created; null leaves the organization's spaces as they are.
    /// </summary>
    public OrganizationSetupSpace? Space { get; set; }

    [Required]
    public OrganizationSetupTemplate? Template { get; set; }
}

public record OrganizationSetupSpace
{
    /// <summary>
    /// The space's current key, as <see cref="OrganizationSetupDto.Space"/> returned it.
    /// </summary>
    [Required]
    public required string Key { get; set; }

    [MaxLength(128)]
    [MinLength(3)]
    public required string Name { get; set; }

    [MaxLength(3)]
    [MinLength(3)]
    public required string NewKey { get; set; }
}

/// <summary>
/// What the setup window starts from - present on <see cref="OrganizationDto.Setup"/> only while the organization
/// isn't set up and the user can update it.
/// </summary>
public record OrganizationSetupDto
{
    /// <summary>
    /// The space to name: the organization's oldest active one, normally the one sign-up created. Null when the
    /// organization has no active space.
    /// </summary>
    public required OrganizationSetupSpaceDto? Space { get; set; }
}

public record OrganizationSetupSpaceDto
{
    public required string Key { get; set; }
    public required string Name { get; set; }
}
