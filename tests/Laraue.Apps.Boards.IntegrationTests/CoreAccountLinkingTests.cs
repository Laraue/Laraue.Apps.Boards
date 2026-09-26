using Grpc.Core;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Identity.Internal.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Attribute = Laraue.Apps.Boards.DataAccess.Models.Attribute;
using Status = Laraue.Apps.Boards.DataAccess.Models.Status;

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

    [Fact]
    public async Task LinkTelegramAccount_ShouldRefuse_WhenOwnerAddedSpaceToPersonalOrganization()
    {
        using var testScope = host.CreateTestScope();

        var outcome = await LinkTelegramOfModifiedOwnerAsync(testScope, 511, async (db, personal) =>
        {
            db.Spaces.Add(new Space
            {
                Name = "Work",
                Color = "#123456",
                Key = "WRK",
                CreatorId = personal.OwnerId,
                OrganizationId = personal.OrganizationId,
            });
            await db.SaveChangesAsync();
        });

        Assert.Equal(AccountLinkOutcome.OwnerHasData, outcome);
    }

    [Fact]
    public async Task LinkTelegramAccount_ShouldRefuse_WhenOwnerAddedBoardToPersonalOrganization()
    {
        using var testScope = host.CreateTestScope();

        var outcome = await LinkTelegramOfModifiedOwnerAsync(testScope, 512, async (db, personal) =>
        {
            db.Epics.Add(new Epic
            {
                Name = "Sprint",
                Color = "#123456",
                UserId = personal.OwnerId,
                SpaceId = personal.SpaceId,
            });
            await db.SaveChangesAsync();
        });

        Assert.Equal(AccountLinkOutcome.OwnerHasData, outcome);
    }

    [Fact]
    public async Task LinkTelegramAccount_ShouldRefuse_WhenOwnerAddedStatusToPersonalOrganization()
    {
        using var testScope = host.CreateTestScope();

        var outcome = await LinkTelegramOfModifiedOwnerAsync(testScope, 513, async (db, personal) =>
        {
            db.Statuses.Add(new Status { Name = "Done", Color = "#123456", EpicId = personal.EpicId, SortOrder = 1 });
            await db.SaveChangesAsync();
        });

        Assert.Equal(AccountLinkOutcome.OwnerHasData, outcome);
    }

    [Fact]
    public async Task LinkTelegramAccount_ShouldRefuse_WhenOwnerDefinedAttributeInPersonalOrganization()
    {
        using var testScope = host.CreateTestScope();

        var outcome = await LinkTelegramOfModifiedOwnerAsync(testScope, 514, async (db, personal) =>
        {
            db.Attributes.Add(new Attribute
            {
                Name = "Priority",
                Color = "#123456",
                AttributeType = AttributeType.Text,
                OrganizationId = personal.OrganizationId,
            });
            await db.SaveChangesAsync();
        });

        Assert.Equal(AccountLinkOutcome.OwnerHasData, outcome);
    }

    [Fact]
    public async Task LinkTelegramAccount_ShouldRefuse_WhenOwnerInvitedMemberToPersonalOrganization()
    {
        using var testScope = host.CreateTestScope();
        var memberId = await testScope.CreateUser();

        var outcome = await LinkTelegramOfModifiedOwnerAsync(testScope, 515, async (db, personal) =>
        {
            db.OrganizationUsers.Add(new OrganizationUser
            {
                OrganizationId = personal.OrganizationId,
                UserId = memberId,
                CanRead = true,
            });
            await db.SaveChangesAsync();
        });

        Assert.Equal(AccountLinkOutcome.OwnerHasData, outcome);
    }

    /// <summary>
    /// Signs up a Telegram user (who gets a personal organization), lets <paramref name="modify"/>
    /// change that organization, then has a new Google user try to connect the same Telegram account.
    /// </summary>
    private static async Task<AccountLinkOutcome> LinkTelegramOfModifiedOwnerAsync(
        WebApiTestHostScope testScope,
        long telegramId,
        Func<DatabaseContext, PersonalOrganization, Task> modify)
    {
        var service = testScope.Services.GetRequiredService<ICoreUserService>();
        var ownerId = await service.CreateIfTelegramIdNotExists(TelegramProfile(telegramId), default);
        var personal = await testScope.Database.Epics
            .Where(x => x.Space!.Organization!.OwnerId == ownerId && x.IsDefault && x.Space.IsDefault)
            .Select(x => new PersonalOrganization(ownerId, x.Space!.OrganizationId, x.SpaceId, x.Id))
            .SingleAsync();
        await modify(testScope.Database, personal);
        var userId = await service.CreateIfGoogleSubjectNotExists(GoogleProfile($"google-{telegramId}"), default);

        return await service.LinkTelegramAccount(userId, TelegramProfile(telegramId), default);
    }

    private sealed record PersonalOrganization(Guid OwnerId, long OrganizationId, long SpaceId, long EpicId);

    private static TelegramUserProfile TelegramProfile(long telegramId) =>
        new(telegramId, $"user{telegramId}", "Ada", "Lovelace", "en");

    private static GoogleUserProfile GoogleProfile(string googleSubject) =>
        new(googleSubject, $"{googleSubject}@example.com", "Ada Lovelace", "Ada", "Lovelace", "en");
}
