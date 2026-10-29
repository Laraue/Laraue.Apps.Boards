using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services.Members;
using Laraue.Apps.Boards.Services.Sources;
using Laraue.Telegram.NET.Testing;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// An issue's Telegram source (BRD-324): the chat's username kept up to date by the bot, and the source
/// <see cref="IssueSourceReader"/> builds from it for the web API and MCP <c>get_issue</c>.
/// </summary>
public class TelegramMessageSourceTests : TelegramIntegrationTest
{
    private static readonly DateTime SentAt = new(2026, 10, 20, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleSaveModeSelected_ShouldStoreChatUsername_WhenChatIsPublic()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var userId = await testScope.CreateUser(x => x.TelegramId = AdminUser.Id);
        var organization = await testScope.InitializeOrganization(userId);
        var status = organization.GetStatus(0, 0, 0);

        var chat = new Chat { Id = -1001111111111, Type = ChatType.Supergroup, Title = "Team", Username = "team_chat" };

        await host.SendUpdateAsync(new Update
        {
            CallbackQuery = new CallbackQuery
            {
                Id = "1",
                From = AdminUser,
                Message = new Message { Id = 42, Chat = chat },
                Data = $"/link/save-mode/{status.Id}/0",
            }
        });

        var linkedChat = Assert.Single(await testScope.Database.LinkedTelegramChats.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal("team_chat", linkedChat.Username);
    }

    [Fact]
    public async Task HandleGroupMessage_ShouldRefreshChatUsername_WhenItChanged()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var chatId = -1002222222222;
        await LinkChat(testScope, chatId, username: "old_name");

        // The group renamed its @username, then made itself private.
        await SendGroupMessage(host, new Chat { Id = chatId, Type = ChatType.Supergroup, Username = "new_name" }, 1);
        Assert.Equal("new_name", await GetUsername(testScope, chatId));

        await SendGroupMessage(host, new Chat { Id = chatId, Type = ChatType.Supergroup }, 2);
        Assert.Null(await GetUsername(testScope, chatId));
    }

    [Fact]
    public async Task GetSource_ShouldReturnPublicLink_WhenIssueWasCreatedInPublicSupergroup()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var chatId = -1003333333333;
        var organizationId = await LinkChat(testScope, chatId, title: "Team");
        await SendGroupMessage(host, new Chat { Id = chatId, Type = ChatType.Supergroup, Username = "team_chat" }, 7);

        var source = await GetSingleIssueSource(testScope, organizationId);

        Assert.False(source.IsPrivateChat);
        Assert.Equal("Team", source.ChatTitle);
        Assert.Equal(await GetDisplayName(testScope, organizationId), source.Sender?.DisplayName);
        Assert.Equal(SentAt, source.SentAt);
        Assert.Equal("https://t.me/team_chat/7", source.MessageUrl);
    }

    [Fact]
    public async Task GetSource_ShouldReturnPrivateSupergroupLink_WhenSupergroupHasNoUsername()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var chatId = -1004444444444;
        var organizationId = await LinkChat(testScope, chatId, title: "Team");
        await SendGroupMessage(host, new Chat { Id = chatId, Type = ChatType.Supergroup }, 7);

        var source = await GetSingleIssueSource(testScope, organizationId);

        Assert.Equal("https://t.me/c/4444444444/7", source.MessageUrl);
    }

    [Fact]
    public async Task GetSource_ShouldReturnNoLink_WhenIssueWasCreatedInBasicGroup()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var chatId = -555555555L;
        var organizationId = await LinkChat(testScope, chatId, title: "Team");
        await SendGroupMessage(host, new Chat { Id = chatId, Type = ChatType.Group }, 7);

        var source = await GetSingleIssueSource(testScope, organizationId);

        Assert.False(source.IsPrivateChat);
        Assert.Equal("Team", source.ChatTitle);
        Assert.Null(source.MessageUrl);
    }

    [Fact]
    public async Task GetSource_ShouldReturnPrivateChatWithoutLink_WhenIssueWasCreatedInPrivateChatWithBot()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        // A private chat's id is the user's own Telegram id; its title is the user's username.
        var organizationId = await LinkChat(testScope, AdminUser.Id, title: AdminUser.Username);
        await host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = AdminUser,
                Id = 7,
                Text = "Buy milk",
                Date = SentAt,
                Chat = new Chat { Id = AdminUser.Id, Type = ChatType.Private },
            }
        });

        var source = await GetSingleIssueSource(testScope, organizationId);

        Assert.True(source.IsPrivateChat);
        Assert.Null(source.ChatTitle);
        Assert.Null(source.MessageUrl);
    }

    [Fact]
    public async Task GetSource_ShouldReturnNull_WhenIssueWasNotCreatedFromTelegram()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var userId = await testScope.CreateUser(x => x.TelegramId = AdminUser.Id);
        var organization = await testScope.InitializeOrganization(userId, org => org
            .AddIssueToDefaultStatus(userId, issue => issue.WithContent("From the web")));

        var source = await new IssueSourceReader(testScope.Database, new MemberProfileReader(testScope.Database))
            .GetSource(organization.GetIssueData(0, 0, 0, 0).Issue.Id, organization.Id, CancellationToken.None);

        Assert.Null(source);
    }

    [Fact]
    public async Task GetSource_ShouldLinkOnlyNewMessages_WhenBasicGroupWasUpgradedToSupergroup()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var oldChatId = -666666666L;
        var newChatId = -1006666666666;
        var organizationId = await LinkChat(testScope, oldChatId, title: "Team");

        await SendGroupMessage(host, new Chat { Id = oldChatId, Type = ChatType.Group }, 7);

        await host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                Id = 8,
                Chat = new Chat { Id = oldChatId, Type = ChatType.Group },
                MigrateToChatId = newChatId,
            }
        });

        // The supergroup numbers its messages anew - the same id 7 is a different message there.
        await SendGroupMessage(host, new Chat { Id = newChatId, Type = ChatType.Supergroup }, 7);

        var reader = new IssueSourceReader(testScope.Database, new MemberProfileReader(testScope.Database));
        var issueIds = await testScope.Database.Issues
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsyncLinqToDB();
        Assert.Equal(2, issueIds.Count);

        var oldSource = await reader.GetSource(issueIds[0], organizationId, CancellationToken.None);
        var newSource = await reader.GetSource(issueIds[1], organizationId, CancellationToken.None);

        Assert.Equal("Team", oldSource!.ChatTitle);
        Assert.Null(oldSource.MessageUrl);
        Assert.Equal("https://t.me/c/6666666666/7", newSource!.MessageUrl);
    }

    private static async Task<long> LinkChat(
        AppTelegramTestHost.AppTelegramTestHostScope testScope,
        long chatId,
        string? title = null,
        string? username = null)
    {
        var userId = await testScope.CreateUser(x => x.TelegramId = AdminUser.Id);
        var organization = await testScope.InitializeOrganization(userId);

        testScope.Database.Add(new LinkedTelegramChat
        {
            ExternalChatId = chatId,
            Title = title,
            Username = username,
            StatusId = organization.GetStatus(0, 0, 0).Id,
            OwnerId = userId,
            SaveMode = SaveMode.EachMessage,
            LinkedAt = DateTime.UtcNow,
        });
        await testScope.Database.SaveChangesAsync();

        return organization.Id;
    }

    private static Task SendGroupMessage(AppTelegramTestHost host, Chat chat, int messageId)
    {
        return host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = AdminUser,
                Id = messageId,
                Text = $"Message {messageId} in {chat.Id}",
                Date = SentAt,
                Chat = chat,
            }
        });
    }

    private static Task<string?> GetUsername(AppTelegramTestHost.AppTelegramTestHostScope testScope, long chatId)
    {
        return testScope.Database.LinkedTelegramChats
            .Where(x => x.ExternalChatId == chatId)
            .Select(x => x.Username)
            .SingleAsyncLinqToDB();
    }

    private static Task<string> GetDisplayName(AppTelegramTestHost.AppTelegramTestHostScope testScope, long organizationId)
    {
        return testScope.Database.OrganizationUsers
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => x.DisplayName)
            .SingleAsyncLinqToDB();
    }

    private static async Task<IssueSourceDto> GetSingleIssueSource(AppTelegramTestHost.AppTelegramTestHostScope testScope, long organizationId)
    {
        var issueId = Assert.Single(await testScope.Database.Issues
            .Select(x => x.Id)
            .ToListAsyncLinqToDB());

        var source = await new IssueSourceReader(testScope.Database, new MemberProfileReader(testScope.Database))
            .GetSource(issueId, organizationId, CancellationToken.None);

        Assert.NotNull(source);
        return source;
    }
}
