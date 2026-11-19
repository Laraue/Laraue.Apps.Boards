using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.TelegramServices.Services.Messages;
using Laraue.Telegram.NET.Testing;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using User = Telegram.Bot.Types.User;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// BRD-367: "💡 Did you know?" tips, sent once when the way someone uses a chat shows a setting would help.
/// </summary>
public class TelegramBotTipsTests : TelegramIntegrationTest
{
    private static Chat DefaultUserChat => new() { Id = DefaultUser.Id, Type = ChatType.Private };

    private static Chat Group => new() { Id = -100777, Type = ChatType.Group, Title = "Team" };

    [Fact]
    public async Task HandleTextMessage_ShouldTipReactions_WhenUserSavesALotWithLinkReplies()
    {
        using var host = GetTelegramTestHost();

        // A new user's private chat replies with the card link. One card short of the threshold: no tip yet.
        await SendNotes(host, DefaultUser, DefaultUserChat, BotTipsService.ReactionsCardCount - 1);
        Assert.Empty(Tips(host));

        await SendNotes(host, DefaultUser, DefaultUserChat, 1, firstId: BotTipsService.ReactionsCardCount);

        var tip = Assert.Single(Tips(host));
        Assert.Equal(DefaultUser.Id, tip.ChatId.Identifier);
        Assert.StartsWith("💡 Did you know? If you save a lot", tip.Text);
        Assert.Equal(
            "https://boards.example.com/documentation/integrations/telegram-chat-settings#heavy-daily-use",
            ReadMoreUrl(tip));

        var db = host.CreateScope().GetDatabaseContext();
        var row = Assert.Single(await db.UserBotMessages.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal(BotMessageKind.TipReactions, row.Kind);
        Assert.Equal(BotMessageStatus.Sent, row.Status);
    }

    [Fact]
    public async Task HandleTextMessage_ShouldTipOnlyOnce_WhenUserKeepsSaving()
    {
        using var host = GetTelegramTestHost();

        await SendNotes(host, DefaultUser, DefaultUserChat, BotTipsService.ReactionsCardCount * 2);

        Assert.Single(Tips(host));
    }

    [Fact]
    public async Task HandleTextMessage_ShouldNotTip_WhenAnotherTipWasSentRecently()
    {
        using var host = GetTelegramTestHost();

        // The first note signs the user up; then another tip was given yesterday.
        await SendNotes(host, DefaultUser, DefaultUserChat, 1);
        var scope = host.CreateScope();
        var db = scope.GetDatabaseContext();
        var userId = Assert.Single(await db.Users.AsNoTracking().ToListAsyncLinqToDB()).Id;
        db.UserBotMessages.Add(new UserBotMessage
        {
            UserId = userId,
            Kind = BotMessageKind.TipSaveOnlyViaSave,
            SentAt = DateTime.UtcNow.AddDays(-1),
            Status = BotMessageStatus.Sent,
        });
        await db.SaveChangesAsync();

        await SendNotes(host, DefaultUser, DefaultUserChat, BotTipsService.ReactionsCardCount, firstId: 2);

        Assert.Empty(Tips(host));
    }

    [Fact]
    public async Task HandleTextMessage_ShouldLinkRussianDocs_WhenUserWritesInRussian()
    {
        using var host = GetTelegramTestHost();
        var user = DefaultUser;
        user.LanguageCode = "ru";

        await SendNotes(host, user, DefaultUserChat, BotTipsService.ReactionsCardCount);

        var tip = Assert.Single(Tips(host));
        Assert.StartsWith("💡 А вы знали?", tip.Text);
        Assert.Equal(
            "https://boards.example.com/ru/documentation/integrations/telegram-chat-settings#heavy-daily-use",
            ReadMoreUrl(tip));
    }

    [Fact]
    public async Task HandleGroupMessage_ShouldTipSaveOnlyViaSave_ToTheOwnerOfABusyGroup()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser(x => x.TelegramId = AdminUser.Id);
        var organization = await testScope.InitializeOrganization(ownerId);
        // Reactions, so only the group tip applies.
        await LinkGroup(testScope, organization, ownerId);

        await SendNotes(host, AdminUser, Group, BotTipsService.SaveOnlyViaSaveCardCount - 1);
        Assert.Empty(Tips(host));

        await SendNotes(host, AdminUser, Group, 1, firstId: BotTipsService.SaveOnlyViaSaveCardCount);

        // In the owner's private chat, not in the group.
        var tip = Assert.Single(Tips(host));
        Assert.Equal(AdminUser.Id, tip.ChatId.Identifier);
        Assert.StartsWith("💡 Did you know? In a busy group", tip.Text);
        Assert.Equal(
            "https://boards.example.com/documentation/integrations/telegram-chat-settings#team-group",
            ReadMoreUrl(tip));
    }

    [Fact]
    public async Task HandleGroupMessage_ShouldNotTip_WhenSenderDidNotLinkTheGroup()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser(x => x.TelegramId = AdminUser.Id);
        var memberId = await testScope.CreateUser(x => x.TelegramId = MemberUser.Id);
        var organization = await testScope.InitializeOrganization(
            ownerId,
            o => o.AddUser(memberId, b => b.SetGlobalAccessLevel(x =>
            {
                x.CanRead = true;
                x.CanCreateIssues = true;
            })));
        await LinkGroup(testScope, organization, ownerId);

        // A busy group, but the member can't change its settings.
        await SendNotes(host, MemberUser, Group, BotTipsService.SaveOnlyViaSaveCardCount);

        Assert.Empty(Tips(host));
    }

    private static async Task LinkGroup(
        AppTelegramTestHost.AppTelegramTestHostScope testScope,
        DataAccess.Models.Organization organization,
        Guid ownerId)
    {
        testScope.Database.Add(new LinkedTelegramChat
        {
            ExternalChatId = Group.Id,
            OrganizationId = organization.Id,
            DestinationType = LinkDestinationType.Status,
            DestinationId = organization.GetStatus(0, 0, 0).Id,
            OwnerId = ownerId,
            SaveMode = SaveMode.EachMessage,
            ReplyMode = ReplyMode.Reaction,
            LinkedAt = DateTime.UtcNow,
        });
        await testScope.Database.SaveChangesAsync();
    }

    private static async Task SendNotes(AppTelegramTestHost host, User from, Chat chat, int count, int firstId = 1)
    {
        for (var id = firstId; id < firstId + count; id++)
        {
            await host.SendUpdateAsync(new Update
            {
                // A card from Telegram is created at the message's time: the tips count recent cards.
                Message = new Message { From = from, Id = id, Text = $"Note {id}", Chat = chat, Date = DateTime.UtcNow },
            });
        }
    }

    private static List<SendMessageRequest> Tips(AppTelegramTestHost host)
    {
        return host.Requests().OfType<SendMessageRequest>().Where(x => x.Text.StartsWith("💡")).ToList();
    }

    private static string? ReadMoreUrl(SendMessageRequest tip)
    {
        return Assert.Single(Assert.Single(Assert.IsType<InlineKeyboardMarkup>(tip.ReplyMarkup).InlineKeyboard)).Url;
    }
}
