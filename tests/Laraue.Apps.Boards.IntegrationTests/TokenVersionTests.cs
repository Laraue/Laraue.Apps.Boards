using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Apps.Retro.WebApiHost.Controllers;
using Laraue.Apps.Retro.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// A token carrying an older <c>User.TokenVersion</c> than the user's current one is rejected on
/// every web host (BRD-222). Merging a user through account linking, which bumps the version, is
/// covered in <see cref="ConnectedAccountsControllerTests"/>.
/// </summary>
[Collection("IntegrationTest")]
public class TokenVersionTests(WebApiTestHost host, RetroWebApiTestHost retroHost)
    : IClassFixture<WebApiTestHost>, IClassFixture<RetroWebApiTestHost>
{
    [Fact]
    public async Task GetUser_ShouldReturnUnauthorized_WhenUserTokenVersionIsStale()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        await BumpTokenVersionAsync(testScope, userId);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => host
            .Controller<UserController>()
            .WithUserAuthorization(userId, tokenVersion: 0)
            .Execute(x => x.GetAsync(default)));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task GetUser_ShouldSucceed_WhenUserTokenHasCurrentVersion()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        await BumpTokenVersionAsync(testScope, userId);

        await host
            .Controller<UserController>()
            .WithUserAuthorization(userId, tokenVersion: 1)
            .Execute(x => x.GetAsync(default));
    }

    [Fact]
    public async Task GetUser_ShouldSucceed_WhenTokenHasNoVersionClaim()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();

        await host
            .Controller<UserController>()
            .WithAuthorizationToken(CreateUserTokenWithoutVersion(userId))
            .Execute(x => x.GetAsync(default));
    }

    [Fact]
    public async Task GetUser_ShouldReturnUnauthorized_WhenUserDoesNotExist()
    {
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => host
            .Controller<UserController>()
            .WithUserAuthorization(Guid.NewGuid())
            .Execute(x => x.GetAsync(default)));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task GetSpaces_ShouldReturnUnauthorized_WhenOrganizationTokenVersionIsStale()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        await BumpTokenVersionAsync(testScope, userId);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => host
            .Controller<SpacesController>()
            .WithOrganizationAuthorization(organization.Id, userId, tokenVersion: 0)
            .Execute(x => x.GetAll(default)));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task GetSpaces_ShouldSucceed_WhenOrganizationTokenHasCurrentVersion()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        await BumpTokenVersionAsync(testScope, userId);

        var spaces = await host
            .Controller<SpacesController>()
            .WithOrganizationAuthorization(organization.Id, userId, tokenVersion: 1)
            .Execute(x => x.GetAll(default));

        Assert.NotEmpty(spaces);
    }

    [Fact]
    public async Task GetRetros_ShouldReturnUnauthorized_WhenOrganizationTokenVersionIsStale()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        await BumpTokenVersionAsync(testScope, userId);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => retroHost
            .Controller<RetroController>(host.Services)
            .WithOrganizationAuthorization(organization.Id, userId, tokenVersion: 0)
            .Execute(x => x.Get(RetrosPage(), default)));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task GetRetros_ShouldSucceed_WhenOrganizationTokenHasCurrentVersion()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        await BumpTokenVersionAsync(testScope, userId);

        await retroHost
            .Controller<RetroController>(host.Services)
            .WithOrganizationAuthorization(organization.Id, userId, tokenVersion: 1)
            .Execute(x => x.Get(RetrosPage(), default));
    }

    private static Task BumpTokenVersionAsync(WebApiTestHostScope testScope, Guid userId)
    {
        return testScope.Database.Users
            .Where(x => x.Id == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1));
    }

    private static GetRetrosRequest RetrosPage()
    {
        return new GetRetrosRequest { Pagination = new PaginationData { Page = 0, PerPage = 10 } };
    }

    /// <summary>
    /// A user token as <see cref="AuthService"/> issued them before the version claim was added.
    /// </summary>
    private string CreateUserTokenWithoutVersion(Guid userId)
    {
        var key = host.Services.GetRequiredService<IOptions<AuthOptions>>().Value.Key;
        var jwt = new JwtSecurityToken(
            issuer: AuthService.Issuer,
            audience: AuthService.UserAudience,
            claims: [new Claim("id", userId.ToString())],
            signingCredentials: new SigningCredentials(
                AuthService.GetSymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
