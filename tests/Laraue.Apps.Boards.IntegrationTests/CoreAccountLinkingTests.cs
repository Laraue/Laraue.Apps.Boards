using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Guarantees of the two-step linking in <see cref="ICoreUserService"/> that can't be reached through
/// the HTTP API. Linking scenarios themselves are covered end to end in
/// <see cref="ConnectedAccountsControllerTests"/>.
/// </summary>
[Collection("IntegrationTest")]
public class CoreAccountLinkingTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task ConnectTelegram_ShouldCompleteLink_WhenRepeatedAfterBoardsStepDidNotRun()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();
        var ownerId = await service.CreateIfTelegramIdNotExists(TelegramProfile(516), default);
        var userId = await service.CreateIfGoogleSubjectNotExists(GoogleProfile("google-516"), default);
        await service.LinkTelegramAccountInIdentity(userId, TelegramProfile(516), default);

        var response = await testScope.Services.GetRequiredService<IConnectedAccountsService>()
            .ConnectTelegram(userId, TelegramProfile(516), default);

        Assert.Equal(AccountLinkOutcome.Linked, response.Outcome);
        Assert.Null((await testScope.Database.Users.SingleAsync(x => x.Id == ownerId)).TelegramId);
        Assert.Equal(516, (await testScope.Database.Users.SingleAsync(x => x.Id == userId)).TelegramId);
        var chat = await testScope.Database.LinkedTelegramChats.SingleAsync(x => x.ExternalChatId == 516);
        Assert.Equal(userId, chat.OwnerId);
    }

    [Fact]
    public async Task ApplyTelegramAccountLink_ShouldThrow_WhenCalledOutsideTransaction()
    {
        using var testScope = host.CreateTestScope();
        var service = testScope.Services.GetRequiredService<ICoreUserService>();
        var userId = await service.CreateIfGoogleSubjectNotExists(GoogleProfile("google-518"), default);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApplyTelegramAccountLink(userId, TelegramProfile(518), default));
        Assert.Null((await testScope.Database.Users.SingleAsync(x => x.Id == userId)).TelegramId);
    }

    private static TelegramUserProfile TelegramProfile(long telegramId) =>
        new(telegramId, $"user{telegramId}", "Ada", "Lovelace", "en");

    private static GoogleUserProfile GoogleProfile(string googleSubject) =>
        new(googleSubject, $"{googleSubject}@example.com", "Ada Lovelace", "Ada", "Lovelace", "en");
}
