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

    public static OrganizationAuthData GetOrganizationAuthData(this ClaimsPrincipal principal)
    {
        var idClaim = principal.Claims.FirstOrDefault(x => x.Type == "orgId")
            ?? throw new UnauthorizedException("User does not contain Org ID claim");

        var userClaim = principal.Claims.FirstOrDefault(x => x.Type == "id")
            ?? throw new UnauthorizedException("User does not contain ID claim");

        var organizationId = long.Parse(idClaim.Value);

        return new OrganizationAuthData
        {
            OrganizationId = organizationId,
            UserId = Guid.Parse(userClaim.Value),
        };
    }
}
