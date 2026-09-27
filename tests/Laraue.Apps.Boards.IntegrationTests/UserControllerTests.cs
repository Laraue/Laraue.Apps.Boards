using System.Net;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class UserControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<UserController> _userController = host.Controller<UserController>();

    [Fact]
    public async Task UpdateProfile_ShouldChangeDefaultNameInitialsAndColor_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var color = Palette.Colors[^1];

        await _userController
            .WithUserAuthorization(userId)
            .Execute(x => x.UpdateProfile(
                new UpdateUserProfileRequest { DisplayName = " Ivan Petrov ", Color = color },
                default));

        var user = await _userController
            .WithUserAuthorization(userId)
            .Execute(x => x.GetAsync(default));

        Assert.Equal("Ivan Petrov", user!.DisplayName);
        Assert.Equal("IP", user.Initials);
        Assert.Equal(color, user.Color);
    }

    [Fact]
    public async Task UpdateProfile_ShouldReturn400_WhenColorIsNotInPalette()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _userController
            .WithUserAuthorization(userId)
            .Execute(x => x.UpdateProfile(
                new UpdateUserProfileRequest { DisplayName = "Ivan", Color = "#123456" },
                default)));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_ShouldReturn400_WhenNameIsBlank()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _userController
            .WithUserAuthorization(userId)
            .Execute(x => x.UpdateProfile(
                new UpdateUserProfileRequest { DisplayName = "  ", Color = Palette.FirstColor },
                default)));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }
}
