using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class CoreUserServiceTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task CreateIfGoogleSubjectNotExists_ShouldCreateGoogleOnlyUser_WhenGoogleSubjectIsNew()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();

        var userId = await service.CreateIfGoogleSubjectNotExists(
            new GoogleUserProfile("google-1", "john.smith@example.com", "John Smith", "John", "Smith", "en"),
            default);

        var user = await testScope.Database.Users.SingleAsync(x => x.Id == userId);
        Assert.Equal("google-1", user.GoogleSubject);
        Assert.Null(user.TelegramId);
        Assert.NotEqual(Guid.Empty, user.GlobalUserId);
        Assert.Equal("John Smith", user.DisplayName);
        Assert.Equal("JS", user.Initials);
    }

    [Fact]
    public async Task CreateIfGoogleSubjectNotExists_ShouldCreatePersonalOrganizationFromEmail_WhenGoogleSubjectIsNew()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();

        var userId = await service.CreateIfGoogleSubjectNotExists(
            new GoogleUserProfile("google-2", "John.Smith+boards@example.com", "John Smith", "John", "Smith", "ru"),
            default);

        var organization = await testScope.Database.Organizations.SingleAsync(x => x.OwnerId == userId);
        Assert.Equal(OrganizationType.Personal, organization.Type);
        Assert.Equal("johnsmithboards", organization.Slug);
        Assert.Equal("Личное", organization.Name);
    }

    [Fact]
    public async Task CreateIfGoogleSubjectNotExists_ShouldStoreInterfaceLanguage_WhenLanguageIsSupported()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();

        var userId = await service.CreateIfGoogleSubjectNotExists(
            new GoogleUserProfile("google-3", "user@example.com", null, null, null, "ru"),
            default);

        var preferences = await service.GetPreferences(userId, default);
        Assert.Equal("ru", preferences.InterfaceLanguage);
    }

    [Fact]
    public async Task CreateIfGoogleSubjectNotExists_ShouldNotLinkTelegramChat_WhenUserIsGoogleOnly()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();

        var userId = await service.CreateIfGoogleSubjectNotExists(
            new GoogleUserProfile("google-4", "user@example.com", "User", "User", null, "en"),
            default);

        Assert.False(await testScope.Database.LinkedTelegramChats.AnyAsync(x => x.OwnerId == userId));
    }

    [Fact]
    public async Task CreateIfGoogleSubjectNotExists_ShouldUseEmailLocalPart_WhenProfileHasNoName()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();

        var userId = await service.CreateIfGoogleSubjectNotExists(
            new GoogleUserProfile("google-5", "jane@example.com", null, null, null, null),
            default);

        var user = await testScope.Database.Users.SingleAsync(x => x.Id == userId);
        Assert.Equal("jane", user.DisplayName);
        Assert.Equal("JA", user.Initials);
    }

    [Fact]
    public async Task CreateIfGoogleSubjectNotExists_ShouldReturnExistingUser_WhenCalledTwiceForSameGoogleSubject()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();
        var profile = new GoogleUserProfile("google-6", "user@example.com", "User", "User", null, "en");

        var firstId = await service.CreateIfGoogleSubjectNotExists(profile, default);
        var secondId = await service.CreateIfGoogleSubjectNotExists(profile, default);

        Assert.Equal(firstId, secondId);
        Assert.Equal(1, await testScope.Database.Users.CountAsync(x => x.GoogleSubject == "google-6"));
        Assert.Equal(1, await testScope.Database.Organizations.CountAsync(x => x.OwnerId == firstId));
    }
}
