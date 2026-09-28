using Grpc.Core;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Identity.Internal.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class UserControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<UserController> _userController = host.Controller<UserController>();

    [Fact]
    public async Task GetUser_ShouldReturnInitialsFromIdentityProfile_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(user => user.DisplayName = "Ada Lovelace");

        var user = await _userController
            .WithUserAuthorization(userId)
            .Execute(x => x.GetAsync(default));

        Assert.Equal("AD", user!.Initials);
    }

    [Fact]
    public async Task GetUser_ShouldReturnNoInitials_WhenIdentityIsUnavailable()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var globalUserId = await testScope.Database.Users
            .Where(x => x.Id == userId)
            .Select(x => x.GlobalUserId.ToString())
            .SingleAsync();
        Mock.Get(host.Services.GetRequiredService<UserIdentityService.UserIdentityServiceClient>())
            .Setup(x => x.GetUserProfileAsync(
                It.Is<GetUserProfileRequest>(r => r.UserId == globalUserId),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Identity is down")));

        var user = await _userController
            .WithUserAuthorization(userId)
            .Execute(x => x.GetAsync(default));

        Assert.Null(user!.Initials);
        Assert.NotEmpty(user.Palette);
    }
}
