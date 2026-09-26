using System.Net;
using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiHost;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Attribute = Laraue.Apps.Boards.DataAccess.Models.Attribute;
using Status = Laraue.Apps.Boards.DataAccess.Models.Status;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class ConnectedAccountsControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task ConnectTelegram_ShouldConnectTelegram_WhenUserSignedUpWithGoogle()
    {
        using var testScope = host.CreateTestScope();
        var userId = await SignUpWithGoogleAsync(testScope, "google-1");

        var outcome = await ConnectTelegramAsync(userId, 501);

        Assert.Equal(AccountLinkOutcome.Linked, outcome);
        var user = await testScope.Database.Users.SingleAsync(x => x.Id == userId);
        Assert.Equal(501, user.TelegramId);
        Assert.Equal("google-1", user.GoogleSubject);
    }

    [Fact]
    public async Task ConnectTelegram_ShouldLinkPersonalChatToPersonalOrganization_WhenTelegramIsConnected()
    {
        using var testScope = host.CreateTestScope();
        var userId = await SignUpWithGoogleAsync(testScope, "google-2");

        await ConnectTelegramAsync(userId, 502);

        var chat = await testScope.Database.LinkedTelegramChats
            .Include(x => x.Status!.Epic!.Space!.Organization)
            .SingleAsync(x => x.ExternalChatId == 502);
        Assert.Equal(userId, chat.OwnerId);
        Assert.Equal(SaveMode.EachMessage, chat.SaveMode);
        Assert.Equal(OrganizationType.Personal, chat.Status!.Epic!.Space!.Organization!.Type);
        Assert.Equal(userId, chat.Status.Epic.Space.Organization.OwnerId);
    }

    [Fact]
    public async Task ConnectTelegram_ShouldMoveAccount_WhenAnotherUserHasItButNoData()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await SignUpWithTelegramAsync(testScope, 503);
        var userId = await SignUpWithGoogleAsync(testScope, "google-3");

        var outcome = await ConnectTelegramAsync(userId, 503);

        Assert.Equal(AccountLinkOutcome.Linked, outcome);
        var owner = await testScope.Database.Users.SingleAsync(x => x.Id == ownerId);
        Assert.Null(owner.TelegramId);
        Assert.Null(owner.GoogleSubject);
        Assert.NotNull(owner.DeletedAt);
        Assert.Equal(userId, owner.DeletedByUserId);
        Assert.NotNull((await testScope.Database.Organizations.SingleAsync(x => x.OwnerId == ownerId)).DeletedAt);
        Assert.Equal(503, (await testScope.Database.Users.SingleAsync(x => x.Id == userId)).TelegramId);
        var chat = await testScope.Database.LinkedTelegramChats
            .Include(x => x.Status!.Epic!.Space!.Organization)
            .SingleAsync(x => x.ExternalChatId == 503);
        Assert.Equal(userId, chat.OwnerId);
        Assert.Equal(userId, chat.Status!.Epic!.Space!.Organization!.OwnerId);
    }

    [Fact]
    public async Task ConnectTelegram_ShouldReturnOwnerHasData_WhenOwnerHasIssues()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await SignUpWithTelegramAsync(testScope, 504);
        await testScope.InitializeOrganization(ownerId, organization => organization.AddIssueToDefaultStatus(ownerId));
        var userId = await SignUpWithGoogleAsync(testScope, "google-4");

        var outcome = await ConnectTelegramAsync(userId, 504);

        Assert.Equal(AccountLinkOutcome.OwnerHasData, outcome);
        Assert.Equal(504, (await testScope.Database.Users.SingleAsync(x => x.Id == ownerId)).TelegramId);
        Assert.Null((await testScope.Database.Users.SingleAsync(x => x.Id == userId)).TelegramId);
    }

    [Fact]
    public async Task ConnectTelegram_ShouldReturnOwnerHasData_WhenOwnerAddedSpaceToPersonalOrganization()
    {
        using var testScope = host.CreateTestScope();

        var outcome = await ConnectTelegramOfModifiedOwnerAsync(testScope, 511, async (db, personal) =>
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
    public async Task ConnectTelegram_ShouldReturnOwnerHasData_WhenOwnerAddedBoardToPersonalOrganization()
    {
        using var testScope = host.CreateTestScope();

        var outcome = await ConnectTelegramOfModifiedOwnerAsync(testScope, 512, async (db, personal) =>
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
    public async Task ConnectTelegram_ShouldReturnOwnerHasData_WhenOwnerAddedStatusToPersonalOrganization()
    {
        using var testScope = host.CreateTestScope();

        var outcome = await ConnectTelegramOfModifiedOwnerAsync(testScope, 513, async (db, personal) =>
        {
            db.Statuses.Add(new Status { Name = "Done", Color = "#123456", EpicId = personal.EpicId, SortOrder = 1 });
            await db.SaveChangesAsync();
        });

        Assert.Equal(AccountLinkOutcome.OwnerHasData, outcome);
    }

    [Fact]
    public async Task ConnectTelegram_ShouldReturnOwnerHasData_WhenOwnerDefinedAttributeInPersonalOrganization()
    {
        using var testScope = host.CreateTestScope();

        var outcome = await ConnectTelegramOfModifiedOwnerAsync(testScope, 514, async (db, personal) =>
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
    public async Task ConnectTelegram_ShouldReturnOwnerHasData_WhenOwnerInvitedMemberToPersonalOrganization()
    {
        using var testScope = host.CreateTestScope();
        var memberId = await testScope.CreateUser();

        var outcome = await ConnectTelegramOfModifiedOwnerAsync(testScope, 515, async (db, personal) =>
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

    [Fact]
    public async Task ConnectTelegram_ShouldReturnUserHasOtherAccount_WhenUserAlreadyHasAnotherTelegramAccount()
    {
        using var testScope = host.CreateTestScope();
        var userId = await SignUpWithTelegramAsync(testScope, 505);

        var outcome = await ConnectTelegramAsync(userId, 506);

        Assert.Equal(AccountLinkOutcome.UserHasOtherAccount, outcome);
        Assert.Equal(505, (await testScope.Database.Users.SingleAsync(x => x.Id == userId)).TelegramId);
    }

    [Fact]
    public async Task ConnectTelegram_ShouldReturnOwnerUsedByAnotherService_WhenIdentityKeepsAccount()
    {
        using var testScope = host.CreateTestScope();
        Mock.Get(host.Services.GetRequiredService<UserIdentityService.UserIdentityServiceClient>())
            .Setup(x => x.LinkTelegramAccountAsync(
                It.Is<LinkTelegramAccountRequest>(r => r.TelegramId == 507),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(GrpcTestHelpers.AsyncUnaryCallOf(new LinkAccountResponse
            {
                Result = LinkAccountResult.OwnerUsedByAnotherService,
            }));
        var userId = await SignUpWithGoogleAsync(testScope, "google-7");

        var outcome = await ConnectTelegramAsync(userId, 507);

        Assert.Equal(AccountLinkOutcome.OwnerUsedByAnotherService, outcome);
        Assert.Null((await testScope.Database.Users.SingleAsync(x => x.Id == userId)).TelegramId);
        Assert.False(await testScope.Database.LinkedTelegramChats.AnyAsync(x => x.ExternalChatId == 507));
    }

    [Fact]
    public async Task ConnectTelegram_ShouldNotDuplicatePersonalChat_WhenRepeated()
    {
        using var testScope = host.CreateTestScope();
        var userId = await SignUpWithGoogleAsync(testScope, "google-8");

        await ConnectTelegramAsync(userId, 508);
        var outcome = await ConnectTelegramAsync(userId, 508);

        Assert.Equal(AccountLinkOutcome.Linked, outcome);
        Assert.Single(await testScope.Database.LinkedTelegramChats.Where(x => x.ExternalChatId == 508).ToListAsync());
    }

    [Fact]
    public async Task ConnectTelegram_ShouldReturnForbidden_WhenSignatureIsWrong()
    {
        using var testScope = host.CreateTestScope();
        var userId = await SignUpWithGoogleAsync(testScope, "google-9");
        var signed = SignedWidgetData(509);
        var tampered = new TelegramWidgetAuthRequest
        {
            Id = 510,
            FirstName = signed.FirstName,
            AuthDate = signed.AuthDate,
            Hash = signed.Hash,
        };

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => host
            .Controller<ConnectedAccountsController>()
            .WithUserAuthorization(userId)
            .Execute(x => x.ConnectTelegram(tampered, default)));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Null((await testScope.Database.Users.SingleAsync(x => x.Id == userId)).TelegramId);
    }

    [Fact]
    public async Task ConnectTelegram_ShouldReturnUnauthorized_WhenUserIsNotSignedIn()
    {
        using var testScope = host.CreateTestScope();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => host
            .Controller<ConnectedAccountsController>()
            .Execute(x => x.ConnectTelegram(SignedWidgetData(520), default)));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task ConnectGoogle_ShouldConnectGoogle_WhenUserSignedUpWithTelegram()
    {
        using var testScope = host.CreateTestScope();
        var userId = await SignUpWithTelegramAsync(testScope, 521);

        var outcome = await ConnectGoogleAsync(userId, "google-21");

        Assert.Equal(AccountLinkOutcome.Linked, outcome);
        var user = await host.Controller<UserController>().WithUserAuthorization(userId).Execute(x => x.GetAsync(default));
        Assert.True(user!.HasGoogleAccount);
        Assert.Equal(521, user.TelegramId);
    }

    [Fact]
    public async Task ConnectGoogle_ShouldMoveAccount_WhenAnotherUserHasItButNoData()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await SignUpWithGoogleAsync(testScope, "google-22");
        var userId = await SignUpWithTelegramAsync(testScope, 522);

        var outcome = await ConnectGoogleAsync(userId, "google-22");

        Assert.Equal(AccountLinkOutcome.Linked, outcome);
        var owner = await testScope.Database.Users.SingleAsync(x => x.Id == ownerId);
        Assert.Null(owner.GoogleSubject);
        Assert.Null(owner.TelegramId);
        Assert.NotNull(owner.DeletedAt);
        Assert.Equal(userId, owner.DeletedByUserId);
        Assert.NotNull((await testScope.Database.Organizations.SingleAsync(x => x.OwnerId == ownerId)).DeletedAt);
        Assert.Equal("google-22", (await testScope.Database.Users.SingleAsync(x => x.Id == userId)).GoogleSubject);
    }

    [Fact]
    public async Task GetUser_ShouldReturnNotFound_WhenUserWasMergedIntoAnotherOne()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await SignUpWithGoogleAsync(testScope, "google-24");
        var userId = await SignUpWithTelegramAsync(testScope, 524);
        await ConnectGoogleAsync(userId, "google-24");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => host
            .Controller<UserController>()
            .WithUserAuthorization(ownerId)
            .Execute(x => x.GetAsync(default)));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task ConnectGoogle_ShouldKeepOwnerAsRegularUser_WhenOwnerKeepsTelegram()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await SignUpWithTelegramAsync(testScope, 525);
        await ConnectGoogleAsync(ownerId, "google-25");
        var userId = await SignUpWithTelegramAsync(testScope, 526);

        var outcome = await ConnectGoogleAsync(userId, "google-25");

        Assert.Equal(AccountLinkOutcome.Linked, outcome);
        var owner = await testScope.Database.Users.SingleAsync(x => x.Id == ownerId);
        Assert.Equal(525, owner.TelegramId);
        Assert.Null(owner.GoogleSubject);
        Assert.Null(owner.DeletedAt);
        Assert.Null((await testScope.Database.Organizations.SingleAsync(x => x.OwnerId == ownerId)).DeletedAt);
    }

    [Fact]
    public async Task ConnectGoogle_ShouldReturnOwnerHasData_WhenOwnerHasIssues()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await SignUpWithGoogleAsync(testScope, "google-23");
        await testScope.InitializeOrganization(ownerId, organization => organization.AddIssueToDefaultStatus(ownerId));
        var userId = await SignUpWithTelegramAsync(testScope, 523);

        var outcome = await ConnectGoogleAsync(userId, "google-23");

        Assert.Equal(AccountLinkOutcome.OwnerHasData, outcome);
        Assert.Null((await testScope.Database.Users.SingleAsync(x => x.Id == userId)).GoogleSubject);
    }

    [Fact]
    public async Task ConnectGoogle_ShouldReturnForbidden_WhenTokenIsInvalid()
    {
        using var testScope = host.CreateTestScope();
        var userId = await SignUpWithTelegramAsync(testScope, 524);
        host.GoogleIdTokenValidatorMock
            .Setup(x => x.ValidateAsync("invalid-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Google ID token is invalid"));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => host
            .Controller<ConnectedAccountsController>()
            .WithUserAuthorization(userId)
            .Execute(x => x.ConnectGoogle(new ConnectGoogleAccountRequest { IdToken = "invalid-token" }, default)));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    private async Task<Guid> SignUpWithTelegramAsync(WebApiTestHostScope testScope, long telegramId)
    {
        await host.Controller<TelegramAuthController>().Execute(x => x.Authenticate(SignedWidgetData(telegramId), default));

        return await testScope.Database.Users.Where(x => x.TelegramId == telegramId).Select(x => x.Id).SingleAsync();
    }

    private async Task<Guid> SignUpWithGoogleAsync(WebApiTestHostScope testScope, string googleSubject)
    {
        SetupGoogleToken(googleSubject);
        await host.Controller<GoogleAuthController>()
            .Execute(x => x.Authenticate(new GoogleAuthRequest { IdToken = googleSubject }, default));

        return await testScope.Database.Users.Where(x => x.GoogleSubject == googleSubject).Select(x => x.Id).SingleAsync();
    }

    private async Task<AccountLinkOutcome> ConnectTelegramAsync(Guid userId, long telegramId)
    {
        var response = await host.Controller<ConnectedAccountsController>()
            .WithUserAuthorization(userId)
            .Execute(x => x.ConnectTelegram(SignedWidgetData(telegramId), default));

        return response!.Outcome;
    }

    private async Task<AccountLinkOutcome> ConnectGoogleAsync(Guid userId, string googleSubject)
    {
        SetupGoogleToken(googleSubject);
        var response = await host.Controller<ConnectedAccountsController>()
            .WithUserAuthorization(userId)
            .Execute(x => x.ConnectGoogle(new ConnectGoogleAccountRequest { IdToken = googleSubject }, default));

        return response!.Outcome;
    }

    /// <summary>
    /// Signs up a Telegram user (who gets a personal organization), lets <paramref name="modify"/>
    /// change that organization, then has a new Google user try to connect the same Telegram account.
    /// </summary>
    private async Task<AccountLinkOutcome> ConnectTelegramOfModifiedOwnerAsync(
        WebApiTestHostScope testScope,
        long telegramId,
        Func<DatabaseContext, PersonalOrganization, Task> modify)
    {
        var ownerId = await SignUpWithTelegramAsync(testScope, telegramId);
        var personal = await testScope.Database.Epics
            .Where(x => x.Space!.Organization!.OwnerId == ownerId && x.IsDefault && x.Space.IsDefault)
            .Select(x => new PersonalOrganization(ownerId, x.Space!.OrganizationId, x.SpaceId, x.Id))
            .SingleAsync();
        await modify(testScope.Database, personal);
        var userId = await SignUpWithGoogleAsync(testScope, $"google-{telegramId}");

        return await ConnectTelegramAsync(userId, telegramId);
    }

    /// <summary>
    /// The mocked Google validator accepts <paramref name="googleSubject"/> itself as the ID token.
    /// </summary>
    private void SetupGoogleToken(string googleSubject)
    {
        host.GoogleIdTokenValidatorMock
            .Setup(x => x.ValidateAsync(googleSubject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdTokenPayload(googleSubject, $"{googleSubject}@example.com", "Ada Lovelace", "Ada", "Lovelace"));
    }

    /// <summary>
    /// Login widget data signed the way Telegram signs it, with the test bot token - so the request goes
    /// through the real signature check.
    /// </summary>
    private TelegramWidgetAuthRequest SignedWidgetData(long telegramId)
    {
        var botToken = host.Services.GetRequiredService<IConfiguration>()["Telegram:Token"]!;
        var authDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dataCheckString = $"auth_date={authDate}\nfirst_name=Ada\nid={telegramId}";
        var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));
        var hash = Convert.ToHexString(HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))).ToLower();

        return new TelegramWidgetAuthRequest { Id = telegramId, FirstName = "Ada", AuthDate = authDate, Hash = hash };
    }

    private sealed record PersonalOrganization(Guid OwnerId, long OrganizationId, long SpaceId, long EpicId);
}
