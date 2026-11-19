using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Telegram.NET.Testing;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// BRD-364: /link in a linked chat is where its settings are changed - save mode, reply mode and destination.
/// </summary>
public class TelegramLinkSettingsTests : TelegramIntegrationTest
{
    private const int MenuMessageId = 42;

    private static Chat Group => new() { Id = 777, Type = ChatType.Group, Title = "Team" };

    [Fact]
    public async Task HandleSettingsReplyMode_ShouldSaveItAndRedrawMenu_WhenButtonTapped()
    {
        using var host = GetTelegramTestHost();
        await LinkGroup(host, SaveMode.EachMessage, ReplyMode.Reaction);

        var menu = await host.SendCallbackAsync(AdminUser, Group, MenuMessageId, "/link/settings/reply-mode/1");

        Assert.Equal(MenuMessageId, menu.MessageId);
        Assert.Contains("Confirmation: a reply with the card link", menu.Text);
        // The button now offers the way back.
        Assert.Contains(
            ButtonsOf(menu),
            x => x == ("👍 Confirm with a reaction", "/link/settings/reply-mode/0"));

        Assert.Equal(ReplyMode.IssueLink, (await GetChat(host)).ReplyMode);
    }

    [Fact]
    public async Task HandleSettingsSaveMode_ShouldSaveItAndRedrawMenu_WhenButtonTapped()
    {
        using var host = GetTelegramTestHost();
        await LinkGroup(host, SaveMode.EachMessage, ReplyMode.Reaction);

        var menu = await host.SendCallbackAsync(AdminUser, Group, MenuMessageId, "/link/settings/save-mode/1");

        Assert.Contains("Saving: only via /save", menu.Text);
        Assert.Contains(
            ButtonsOf(menu),
            x => x == ("💬 Save every message", "/link/settings/save-mode/0"));

        var chat = await GetChat(host);
        Assert.Equal(SaveMode.BotMentionedMessages, chat.SaveMode);
        // Only the tapped setting changes.
        Assert.Equal(ReplyMode.Reaction, chat.ReplyMode);
    }

    [Fact]
    public async Task HandleSettingsReplyMode_ShouldKeepChosenValue_WhenButtonTappedTwice()
    {
        using var host = GetTelegramTestHost();
        await LinkGroup(host, SaveMode.EachMessage, ReplyMode.Reaction);

        // A double tap sends the same callback twice - it sets the value again rather than toggling it back.
        await host.SendCallbackAsync(AdminUser, Group, MenuMessageId, "/link/settings/reply-mode/1");
        await host.SendCallbackAsync(AdminUser, Group, MenuMessageId, "/link/settings/reply-mode/1");

        Assert.Equal(ReplyMode.IssueLink, (await GetChat(host)).ReplyMode);
    }

    [Fact]
    public async Task HandleSettingsReplyMode_ShouldRefuse_WhenUserIsNotGroupAdmin()
    {
        using var host = GetTelegramTestHost();
        await LinkGroup(host, SaveMode.EachMessage, ReplyMode.Reaction);

        await host.SendUpdateAsync(new Update
        {
            CallbackQuery = new CallbackQuery
            {
                Id = "1",
                From = MemberUser,
                Message = new Message { Id = MenuMessageId, Chat = Group },
                Data = "/link/settings/reply-mode/1",
            }
        });

        Assert.Contains(
            host.Requests().OfType<AnswerCallbackQueryRequest>(),
            x => x.Text == "User should be group admin");
        Assert.Empty(host.Requests().OfType<EditMessageTextRequest>());
        Assert.Equal(ReplyMode.Reaction, (await GetChat(host)).ReplyMode);
    }

    [Fact]
    public async Task HandleChangeDestination_ShouldShowOrganizationPicker_WhenButtonTapped()
    {
        using var host = GetTelegramTestHost();
        var organizationName = await LinkGroup(host, SaveMode.EachMessage, ReplyMode.Reaction);

        var picker = await host.SendCallbackAsync(AdminUser, Group, MenuMessageId, "/link/destination");

        Assert.Equal("Choose organization:", picker.Text);
        Assert.Contains(ButtonsOf(picker), x => x.Text == $"🏢 {organizationName}");

        // Nothing changes until the new destination is picked to the end.
        Assert.Null((await GetChat(host)).UnlinkedAt);
    }

    [Fact]
    public async Task HandleLink_ShouldShowLinkReplyMode_WhenNewUserOpensSettingsInPersonalChat()
    {
        using var host = GetTelegramTestHost();
        var personalChat = new Chat { Id = DefaultUser.Id, Type = ChatType.Private };

        // The first message signs the user up and links their personal chat with the defaults.
        await host.SendUpdateAsync(new Update
        {
            Message = new Message { From = DefaultUser, Id = 1, Text = "First note", Chat = personalChat },
        });
        await host.SendUpdateAsync(new Update
        {
            Message = new Message { From = DefaultUser, Id = 2, Text = "/link", Chat = personalChat },
        });

        var menu = host.Requests().OfType<SendMessageRequest>().Last();
        Assert.Contains("Saving: every message", menu.Text);
        Assert.Contains("Confirmation: a reply with the card link", menu.Text);
        Assert.Contains(
            Assert.IsType<InlineKeyboardMarkup>(menu.ReplyMarkup).InlineKeyboard.SelectMany(row => row),
            x => x.CallbackData == "/link/settings/reply-mode/0");
    }

    [Fact]
    public async Task LinkFlow_ShouldSaveSpaceAsDestination_WhenEpicIsAskedEachTime()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(x => x.TelegramId = AdminUser.Id);
        var organization = await testScope.InitializeOrganization(userId);
        var space = organization.GetSpace(0);

        // "❓ Ask for the epic each time" in the epic picker, then the usual two questions.
        var saveModePicker = await host.SendCallbackAsync(AdminUser, Group, MenuMessageId, $"/link/each-time/1/{space.Id}");
        Assert.StartsWith("Choose how the bot should save messages:", saveModePicker.Text);

        await host.SendCallbackAsync(AdminUser, Group, MenuMessageId, $"/link/save-mode/1/{space.Id}/0");
        var confirmed = await host.SendCallbackAsync(AdminUser, Group, MenuMessageId, $"/link/reply-mode/1/{space.Id}/0/1");
        Assert.Contains($"{organization.Name} → {space.Name} → ❓ chosen each time", confirmed.Text);

        var chat = await GetChat(host);
        Assert.Equal(organization.Id, chat.OrganizationId);
        Assert.Equal(LinkDestinationType.Space, chat.DestinationType);
        Assert.Equal(space.Id, chat.DestinationId);

        // /link then shows the partial destination in the settings menu.
        await host.SendUpdateAsync(new Update
        {
            Message = new Message { From = AdminUser, Id = 2, Text = "/link", Chat = Group },
        });

        var menu = host.Requests().OfType<SendMessageRequest>().Last();
        Assert.StartsWith(
            $"This chat saves to\n{organization.Name} → {space.Name} → ❓ chosen each time\n",
            menu.Text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task LinkFlow_ShouldRefuse_WhenUserCanLinkChatsButCannotReadTheSpace()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser(x => x.TelegramId = AdminUser.Id);
        var memberId = await testScope.CreateUser(x => x.TelegramId = MemberUser.Id);
        // May link chats in the organization, but has no read access to its space.
        var organization = await testScope.InitializeOrganization(
            ownerId,
            o => o.AddUser(memberId, b => b.SetAdminAccessLevel(AdminAccessLevel.LinkChats)));
        var space = organization.GetSpace(0);
        var privateChat = new Chat { Id = MemberUser.Id, Type = ChatType.Private };

        await host.SendUpdateAsync(new Update
        {
            CallbackQuery = new CallbackQuery
            {
                Id = "1",
                From = MemberUser,
                Message = new Message { Id = MenuMessageId, Chat = privateChat },
                Data = $"/link/each-time/1/{space.Id}",
            }
        });

        Assert.Contains(
            host.Requests().OfType<AnswerCallbackQueryRequest>(),
            x => x.Text == "This item no longer exists — it may have been renamed, moved or deleted. Please start over with /link.");
        Assert.Empty(host.Requests().OfType<EditMessageTextRequest>());
    }

    /// <returns>The organization's name.</returns>
    private static async Task<string> LinkGroup(AppTelegramTestHost host, SaveMode saveMode, ReplyMode replyMode)
    {
        var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(x => x.TelegramId = AdminUser.Id);
        var organization = await testScope.InitializeOrganization(userId);

        testScope.Database.Add(new LinkedTelegramChat
        {
            ExternalChatId = Group.Id,
            OrganizationId = organization.Id,
            DestinationType = LinkDestinationType.Status,
            DestinationId = organization.GetStatus(0, 0, 0).Id,
            OwnerId = userId,
            SaveMode = saveMode,
            ReplyMode = replyMode,
            LinkedAt = DateTime.UtcNow,
        });
        await testScope.Database.SaveChangesAsync();

        return organization.Name;
    }

    private static async Task<LinkedTelegramChat> GetChat(AppTelegramTestHost host)
    {
        var db = host.CreateScope().GetDatabaseContext();

        // Any user the bot sees for the first time is signed up with a personal chat of their own, so look up the
        // group's link specifically.
        return Assert.Single(await db.LinkedTelegramChats
            .AsNoTracking()
            .Where(x => x.ExternalChatId == Group.Id)
            .ToListAsyncLinqToDB());
    }

    private static List<(string Text, string? CallbackData)> ButtonsOf(EditMessageTextRequest request)
    {
        return Assert.IsType<InlineKeyboardMarkup>(request.ReplyMarkup).InlineKeyboard
            .SelectMany(row => row)
            .Select(button => (button.Text, button.CallbackData))
            .ToList();
    }
}
