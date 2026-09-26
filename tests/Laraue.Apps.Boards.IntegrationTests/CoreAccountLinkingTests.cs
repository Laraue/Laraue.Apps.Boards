using Grpc.Core;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Identity.Internal.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class CoreAccountLinkingTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task LinkTelegramAccount_ShouldConnectTelegram_WhenUserSignedUpWithGoogle()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();
        var userId = await service.CreateIfGoogleSubjectNotExists(GoogleProfile("google-1"), default);

        var outcome = await service.LinkTelegramAccount(userId, TelegramProfile(501), default);

        Assert.Equal(AccountLinkOutcome.Linked, outcome);
        var user = await testScope.Database.Users.SingleAsync(x => x.Id == userId);
        Assert.Equal(501, user.TelegramId);
        Assert.Equal("google-1", user.GoogleSubject);
    }

    [Fact]
    public async Task LinkTelegramAccount_ShouldLinkPersonalChatToPersonalOrganization_WhenTelegramIsConnected()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();
        var userId = await service.CreateIfGoogleSubjectNotExists(GoogleProfile("google-2"), default);

        await service.LinkTelegramAccount(userId, TelegramProfile(502), default);

        var chat = await testScope.Database.LinkedTelegramChats
            .Include(x => x.Status!.Epic!.Space!.Organization)
            .SingleAsync(x => x.ExternalChatId == 502);
        Assert.Equal(userId, chat.OwnerId);
        Assert.Equal(SaveMode.EachMessage, chat.SaveMode);
        Assert.Equal(OrganizationType.Personal, chat.Status!.Epic!.Space!.Organization!.Type);
        Assert.Equal(userId, chat.Status.Epic.Space.Organization.OwnerId);
    }

    [Fact]
    public async Task LinkTelegramAccount_ShouldMoveAccount_WhenAnotherUserHasItButNoData()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();
        var ownerId = await service.CreateIfTelegramIdNotExists(TelegramProfile(503), default);
        var userId = await service.CreateIfGoogleSubjectNotExists(GoogleProfile("google-3"), default);

        var outcome = await service.LinkTelegramAccount(userId, TelegramProfile(503), default);

        Assert.Equal(AccountLinkOutcome.Linked, outcome);
        Assert.Null((await testScope.Database.Users.SingleAsync(x => x.Id == ownerId)).TelegramId);
        Assert.Equal(503, (await testScope.Database.Users.SingleAsync(x => x.Id == userId)).TelegramId);
        var chat = await testScope.Database.LinkedTelegramChats.SingleAsync(x => x.ExternalChatId == 503);
        Assert.Equal(userId, chat.OwnerId);
    }

    [Fact]
    public async Task LinkTelegramAccount_ShouldRefuse_WhenAnotherUserHasItAndHasData()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();
        var ownerId = await service.CreateIfTelegramIdNotExists(TelegramProfile(504), default);
        await testScope.InitializeOrganization(ownerId, organization => organization.AddIssueToDefaultStatus(ownerId));
        var userId = await service.CreateIfGoogleSubjectNotExists(GoogleProfile("google-4"), default);

        var outcome = await service.LinkTelegramAccount(userId, TelegramProfile(504), default);

        Assert.Equal(AccountLinkOutcome.OwnerHasData, outcome);
        Assert.Equal(504, (await testScope.Database.Users.SingleAsync(x => x.Id == ownerId)).TelegramId);
        Assert.Null((await testScope.Database.Users.SingleAsync(x => x.Id == userId)).TelegramId);
    }

    [Fact]
    public async Task LinkTelegramAccount_ShouldRefuse_WhenUserAlreadyHasAnotherTelegramAccount()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();
        var userId = await service.CreateIfTelegramIdNotExists(TelegramProfile(505), default);

        var outcome = await service.LinkTelegramAccount(userId, TelegramProfile(506), default);

        Assert.Equal(AccountLinkOutcome.UserHasOtherAccount, outcome);
        Assert.Equal(505, (await testScope.Database.Users.SingleAsync(x => x.Id == userId)).TelegramId);
    }

    [Fact]
    public async Task LinkTelegramAccount_ShouldRefuse_WhenIdentityKeepsAccountForAnotherService()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();
        Mock.Get(testScope.Services.GetRequiredService<UserIdentityService.UserIdentityServiceClient>())
            .Setup(x => x.LinkTelegramAccountAsync(
                It.Is<LinkTelegramAccountRequest>(r => r.TelegramId == 507),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(GrpcTestHelpers.AsyncUnaryCallOf(new LinkAccountResponse
            {
                Result = LinkAccountResult.OwnerUsedByAnotherService,
            }));
        var userId = await service.CreateIfGoogleSubjectNotExists(GoogleProfile("google-7"), default);

        var outcome = await service.LinkTelegramAccount(userId, TelegramProfile(507), default);

        Assert.Equal(AccountLinkOutcome.OwnerUsedByAnotherService, outcome);
        Assert.Null((await testScope.Database.Users.SingleAsync(x => x.Id == userId)).TelegramId);
        Assert.False(await testScope.Database.LinkedTelegramChats.AnyAsync(x => x.ExternalChatId == 507));
    }

    [Fact]
    public async Task LinkGoogleAccount_ShouldConnectGoogle_WhenUserSignedUpWithTelegram()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();
        var userId = await service.CreateIfTelegramIdNotExists(TelegramProfile(508), default);

        var outcome = await service.LinkGoogleAccount(userId, GoogleProfile("google-8"), default);

        Assert.Equal(AccountLinkOutcome.Linked, outcome);
        var user = await testScope.Database.Users.SingleAsync(x => x.Id == userId);
        Assert.Equal("google-8", user.GoogleSubject);
        Assert.Equal(508, user.TelegramId);
    }

    [Fact]
    public async Task LinkGoogleAccount_ShouldMoveAccount_WhenAnotherUserHasItButNoData()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();
        var ownerId = await service.CreateIfGoogleSubjectNotExists(GoogleProfile("google-9"), default);
        var userId = await service.CreateIfTelegramIdNotExists(TelegramProfile(509), default);

        var outcome = await service.LinkGoogleAccount(userId, GoogleProfile("google-9"), default);

        Assert.Equal(AccountLinkOutcome.Linked, outcome);
        Assert.Null((await testScope.Database.Users.SingleAsync(x => x.Id == ownerId)).GoogleSubject);
        Assert.Equal("google-9", (await testScope.Database.Users.SingleAsync(x => x.Id == userId)).GoogleSubject);
    }

    [Fact]
    public async Task LinkGoogleAccount_ShouldRefuse_WhenAnotherUserHasItAndHasData()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();
        var ownerId = await service.CreateIfGoogleSubjectNotExists(GoogleProfile("google-10"), default);
        await testScope.InitializeOrganization(ownerId, organization => organization.AddIssueToDefaultStatus(ownerId));
        var userId = await service.CreateIfTelegramIdNotExists(TelegramProfile(510), default);

        var outcome = await service.LinkGoogleAccount(userId, GoogleProfile("google-10"), default);

        Assert.Equal(AccountLinkOutcome.OwnerHasData, outcome);
        Assert.Null((await testScope.Database.Users.SingleAsync(x => x.Id == userId)).GoogleSubject);
    }

    private static TelegramUserProfile TelegramProfile(long telegramId) =>
        new(telegramId, $"user{telegramId}", "Ada", "Lovelace", "en");

    private static GoogleUserProfile GoogleProfile(string googleSubject) =>
        new(googleSubject, $"{googleSubject}@example.com", "Ada Lovelace", "Ada", "Lovelace", "en");
}
