using System.Security.Claims;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Boards.Common;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetId(this ClaimsPrincipal principal)
    {
        var idClaim = principal.Claims.FirstOrDefault(x => x.Type == "id")
            ?? throw new UnauthorizedException("User does not contain ID claim");

        return Guid.Parse(idClaim.Value);
    }

    /// <summary>
    /// The organization of the request's organization token, or null when it carries none - for an
    /// endpoint that works with or without a current organization.
    /// </summary>
    public static long? FindOrganizationId(this ClaimsPrincipal principal)
    {
        var idClaim = principal.Claims.FirstOrDefault(x => x.Type == "orgId");

        return idClaim is null ? null : long.Parse(idClaim.Value);
    }

    public static OrganizationAuthData GetOrganizationAuthData(this ClaimsPrincipal principal)
    {
        var idClaim = principal.Claims.FirstOrDefault(x => x.Type == "orgId")
            ?? throw new UnauthorizedException("User does not contain Org ID claim");

        var userClaim = principal.Claims.FirstOrDefault(x => x.Type == "id")
            ?? throw new UnauthorizedException("User does not contain ID claim");

        var organizationId = long.Parse(idClaim.Value);

        var apiKeyIdClaim = principal.Claims.FirstOrDefault(x => x.Type == "apiKeyId");

        return new OrganizationAuthData
        {
            OrganizationId = organizationId,
            UserId = Guid.Parse(userClaim.Value),
            ApiKeyId = apiKeyIdClaim is null ? null : Guid.Parse(apiKeyIdClaim.Value),
        };
    }
}
