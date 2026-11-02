using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.Services.Attributes;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

/// <summary>
/// Which attributes a space or an epic uses and shows on cards (BRD-179).
/// </summary>
[Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
[ApiController]
public class AttributeSettingsController(IAttributeSettingsService attributeSettingsService)
    : ControllerBase
{
    [HttpGet("/api/spaces/{key}/attribute-settings")]
    public Task<AttributeSettingsDto> GetForSpace(
        string key,
        CancellationToken cancellationToken = default)
    {
        return attributeSettingsService.GetForSpace(
            new SpaceAttributeSettingsRequest
            {
                Key = key,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }

    [HttpPost("/api/spaces/{key}/attribute-settings/override")]
    public Task<AttributeSettingsDto> OverrideSpace(
        string key,
        CancellationToken cancellationToken = default)
    {
        return attributeSettingsService.OverrideSpace(
            new SpaceAttributeSettingsRequest
            {
                Key = key,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }

    [HttpPut("/api/spaces/{key}/attribute-settings")]
    public Task<AttributeSettingsDto> UpdateSpace(
        string key,
        [FromBody] UpdateSpaceAttributeSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        return attributeSettingsService.UpdateSpace(
            request with
            {
                Key = key,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }

    [HttpDelete("/api/spaces/{key}/attribute-settings")]
    public Task<AttributeSettingsDto> ResetSpace(
        string key,
        CancellationToken cancellationToken = default)
    {
        return attributeSettingsService.ResetSpace(
            new SpaceAttributeSettingsRequest
            {
                Key = key,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }

    [HttpGet("/api/epics/{id:long}/attribute-settings")]
    public Task<AttributeSettingsDto> GetForEpic(
        long id,
        CancellationToken cancellationToken = default)
    {
        return attributeSettingsService.GetForEpic(
            new EpicAttributeSettingsRequest
            {
                Id = id,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }

    [HttpPost("/api/epics/{id:long}/attribute-settings/override")]
    public Task<AttributeSettingsDto> OverrideEpic(
        long id,
        CancellationToken cancellationToken = default)
    {
        return attributeSettingsService.OverrideEpic(
            new EpicAttributeSettingsRequest
            {
                Id = id,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }

    [HttpPut("/api/epics/{id:long}/attribute-settings")]
    public Task<AttributeSettingsDto> UpdateEpic(
        long id,
        [FromBody] UpdateEpicAttributeSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        return attributeSettingsService.UpdateEpic(
            request with
            {
                Id = id,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }

    [HttpDelete("/api/epics/{id:long}/attribute-settings")]
    public Task<AttributeSettingsDto> ResetEpic(
        long id,
        CancellationToken cancellationToken = default)
    {
        return attributeSettingsService.ResetEpic(
            new EpicAttributeSettingsRequest
            {
                Id = id,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
}
