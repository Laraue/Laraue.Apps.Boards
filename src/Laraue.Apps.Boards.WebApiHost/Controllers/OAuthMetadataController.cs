using Laraue.Apps.Boards.WebApiServices.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

/// <summary>
/// RFC 8414 authorization server metadata (BRD-362), at the issuer's well-known path - nginx routes
/// <c>https://boards.laraue.com/.well-known/oauth-authorization-server</c> here unchanged, outside <c>/boards-api</c>.
/// </summary>
[AllowAnonymous]
[ApiController]
public class OAuthMetadataController(IOAuthService oauthService) : ControllerBase
{
    [HttpGet("/.well-known/oauth-authorization-server")]
    public OAuthServerMetadata Get()
    {
        return oauthService.GetMetadata();
    }
}
