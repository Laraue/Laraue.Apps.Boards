using System.Net;
using Grpc.Core;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
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

    [Fact]
    public async Task GetProfile_ShouldReturnIdentityProfile_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(user => user.DisplayName = "Ada Lovelace");

        var profile = await _userController
            .WithUserAuthorization(userId)
            .Execute(x => x.GetProfile(default));

        Assert.Equal("Ada Lovelace", profile!.DisplayName);
        Assert.Equal("AD", profile.Initials);
    }

    [Fact]
    public async Task UpdateProfile_ShouldSaveProfileInIdentity_WhenRequestIsValid()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(user => user.DisplayName = "Ada");

        var updated = await _userController
            .WithUserAuthorization(userId)
            .Execute(x => x.UpdateProfile(
                new UpdateProfileRequest
                {
                    GivenName = "Augusta",
                    FamilyName = "King",
                    DisplayName = "Countess of Lovelace",
                },
                default));
        var profile = await _userController
            .WithUserAuthorization(userId)
            .Execute(x => x.GetProfile(default));

        Assert.Equal("Countess of Lovelace", updated!.DisplayName);
        Assert.Equal("CO", updated.Initials);
        Assert.Equal("Augusta", profile!.GivenName);
        Assert.Equal("King", profile.FamilyName);
        Assert.Equal("Countess of Lovelace", profile.DisplayName);
    }

    [Fact]
    public async Task UpdateProfile_ShouldNotSendNames_WhenNamesAreNull()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();

        var profile = await _userController
            .WithUserAuthorization(userId)
            .Execute(x => x.UpdateProfile(
                new UpdateProfileRequest { DisplayName = "Ada" },
                default));

        Assert.Null(profile!.GivenName);
        Assert.Null(profile.FamilyName);
        Assert.Equal("Ada", profile.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateProfile_ShouldReturnBadRequest_WhenDisplayNameIsBlank(string displayName)
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _userController
            .WithUserAuthorization(userId)
            .Execute(x => x.UpdateProfile(
                new UpdateProfileRequest { DisplayName = displayName },
                default)));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_ShouldReturnBadRequest_WhenNamesAreTooLong()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _userController
            .WithUserAuthorization(userId)
            .Execute(x => x.UpdateProfile(
                new UpdateProfileRequest
                {
                    GivenName = new string('a', 129),
                    DisplayName = new string('a', 258),
                },
                default)));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_ShouldReturnServiceUnavailable_WhenIdentityIsUnavailable()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var globalUserId = await testScope.Database.Users
            .Where(x => x.Id == userId)
            .Select(x => x.GlobalUserId.ToString())
            .SingleAsync();
        Mock.Get(host.Services.GetRequiredService<UserIdentityService.UserIdentityServiceClient>())
            .Setup(x => x.UpdateUserProfileAsync(
                It.Is<UpdateUserProfileRequest>(r => r.UserId == globalUserId),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Identity is down")));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _userController
            .WithUserAuthorization(userId)
            .Execute(x => x.UpdateProfile(
                new UpdateProfileRequest { DisplayName = "Ada" },
                default)));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }
}
