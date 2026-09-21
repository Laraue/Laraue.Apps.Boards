using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
[ApiController]
[Route("/api/api-keys")]
public class ApiKeysController(IApiKeysService apiKeysService) : ControllerBase
{
    [HttpPost]
    public Task<CreateApiKeyResponse> Create(
        [FromBody] CreateApiKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        return apiKeysService.Create(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }

    [HttpPost("search")]
    public Task<ShortPaginatedResult<ApiKeyDto>> GetAll(
        [FromBody] GetApiKeysRequest request,
        CancellationToken cancellationToken = default)
    {
        return apiKeysService.GetAll(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }

    [HttpDelete("{id:guid}")]
    public Task Revoke(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return apiKeysService.Revoke(
            new RevokeApiKeyRequest
            {
                Id = id,
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
}
