using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Laraue.Apps.Boards.WebApiServices;

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

    /// <summary>
    /// HS256 (<see cref="SecurityAlgorithms.HmacSha256"/>) requires a key of at least 256 bits.
    /// </summary>
    public const int MinKeyBytes = 32;

    /// <summary>
    /// Both hosts that use the organization/user JWTs call this at startup (to configure token
    /// validation), so a too-short <c>Auth:Key</c> fails there with a clear message in every
    /// environment - instead of on the first sign-in, with IdentityModel's IDX10720 error.
    /// </summary>
    public static SymmetricSecurityKey GetSymmetricSecurityKey(string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        if (keyBytes.Length < MinKeyBytes)
        {
            throw new InvalidOperationException(
                $"Auth:Key must be at least {MinKeyBytes} bytes ({MinKeyBytes * 8} bits) long for HS256, " +
                $"but it is {keyBytes.Length} bytes.");
        }

        return new SymmetricSecurityKey(keyBytes);
    }
}

public class AuthOptions
{
    [Required]
    // Counts characters, not bytes - GetSymmetricSecurityKey does the exact byte check.
    [MinLength(AuthService.MinKeyBytes)]
    public required string Key { get; set; }
}
