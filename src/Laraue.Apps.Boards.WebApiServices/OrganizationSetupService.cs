using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Boards.WebApiServices;

/// <summary>
/// The organization setup window (BRD-293). The web app renames the space and adds the demo data with the regular
/// space, epic and issue endpoints, then completes the setup here so the window isn't shown again.
/// </summary>
public interface IOrganizationSetupService
{
    Task Complete(
        CompleteOrganizationSetupRequest request,
        CancellationToken cancellationToken);
}

public class OrganizationSetupService(
    ICoreOrganizationSetupService coreOrganizationSetupService,
    IAccessService accessService)
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

        var isCompleted = await coreOrganizationSetupService.Complete(
            request.AuthData.OrganizationId,
            request.DemoDataAdded,
            cancellationToken);

        if (!isCompleted)
            throw new BadRequestException(nameof(request.DemoDataAdded), ErrorMessages.OrganizationAlreadySetUp);
    }
}

public record CompleteOrganizationSetupRequest
{
    public OrganizationAuthData AuthData { get; set; }

    /// <summary>
    /// Whether the user added demo data in the window - only for the metric.
    /// </summary>
    public bool DemoDataAdded { get; set; }
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

    /// <summary>
    /// The space's active issues (e.g. saved from Telegram before the setup): changing the key changes their keys
    /// and links, so the window warns when there are any.
    /// </summary>
    public required int IssueCount { get; set; }
}
