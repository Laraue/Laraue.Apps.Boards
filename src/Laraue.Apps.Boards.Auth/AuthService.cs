using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Laraue.Apps.Boards.Auth;

public interface IAuthService
{
    string CreateOrganizationToken(long organizationId, Guid userId);
    string CreateUserToken(Guid userId);
}

public class AuthService(IOptions<AuthOptions> options) : IAuthService
{
    public const string Issuer = "NoteBoardBotBackend";
    public const string OrganizationAudience = "NoteBoardTelegramMiniApp";
    public const string UserAudience = "NoteBoardUserTelegramMiniApp";

    public string CreateOrganizationToken(long organizationId, Guid userId)
    {
        var claims = new List<Claim>
        {
            new("orgId", organizationId.ToString()),
            new("id", userId.ToString()),
        };

        var jwt = new JwtSecurityToken(
            issuer: Issuer,
            audience: OrganizationAudience,
            claims: claims,
            signingCredentials: new SigningCredentials(
                GetSymmetricSecurityKey(options.Value.Key),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    public string CreateUserToken(Guid userId)
    {
        var claims = new List<Claim>
        {
            new("id", userId.ToString())
        };

        var jwt = new JwtSecurityToken(
            issuer: Issuer,
            audience: UserAudience,
            claims: claims,
            signingCredentials: new SigningCredentials(
                GetSymmetricSecurityKey(options.Value.Key),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    public static SymmetricSecurityKey GetSymmetricSecurityKey(string key)
    {
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    }
}

public class AuthOptions
{
    public required string Key { get; set; }
}
