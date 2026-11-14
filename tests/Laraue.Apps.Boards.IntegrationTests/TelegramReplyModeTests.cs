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
/// BRD-361: a new user's personal chat answers a saved message with the card's link instead of a 👍, and the first
/// reply offers to switch back to reactions.
/// </summary>
public class TelegramReplyModeTests : TelegramIntegrationTest
{
    private const string SwitchToReactionsButtonText = "👍 React instead of replying";

    // A private chat's id is the user's own Telegram id.
    private static Chat DefaultUserChat => new() { Id = DefaultUser.Id, Type = ChatType.Private };

    [Fact]
    public async Task HandleTextMessage_ShouldStartNewPersonalChatInLinkMode_WhenUserSignsUp()
    {
        using var host = GetTelegramTestHost();

        await SendText(host, 1, "First note");

        var db = host.CreateScope().GetDatabaseContext();
        var linkedChat = Assert.Single(await db.LinkedTelegramChats.ToListAsyncLinqToDB());
        Assert.Equal(ReplyMode.IssueLink, linkedChat.ReplyMode);
    }

    [Fact]
    public async Task HandleTextMessage_ShouldReplyWithLinkAndOfferReactions_WhenFirstCardInPersonalChat()
    {
        using var host = GetTelegramTestHost();

        await SendText(host, 1, "First note");

        Assert.Empty(host.Requests().OfType<SetMessageReactionRequest>());

        var reply = host.Requests().Single<SendMessageRequest>();
        Assert.Equal(1, reply.ReplyParameters?.MessageId);
        Assert.Contains("First note", reply.Text);

        var rows = Assert.IsType<InlineKeyboardMarkup>(reply.ReplyMarkup).InlineKeyboard.ToList();
        Assert.Equal(2, rows.Count);

        var openButton = Assert.Single(rows[0]);
        Assert.Equal("🔗 Open issue", openButton.Text);
        Assert.NotNull(openButton.Url);

        var switchButton = Assert.Single(rows[1]);
        Assert.Equal(SwitchToReactionsButtonText, switchButton.Text);
        Assert.Equal("/reply-mode/reaction/1", switchButton.CallbackData);
    }

    [Fact]
    public async Task HandleTextMessage_ShouldReplyWithLinkOnly_WhenNotFirstCardInChat()
    {
        using var host = GetTelegramTestHost();

        await SendText(host, 1, "First note");
        await SendText(host, 2, "Second note");

        Assert.Empty(host.Requests().OfType<SetMessageReactionRequest>());

        var secondReply = host.Requests().OfType<SendMessageRequest>().Last();
        Assert.Equal(2, secondReply.ReplyParameters?.MessageId);

        var row = Assert.Single(Assert.IsType<InlineKeyboardMarkup>(secondReply.ReplyMarkup).InlineKeyboard);
        Assert.Equal("🔗 Open issue", Assert.Single(row).Text);
    }

    [Fact]
    public async Task HandleEditedMessage_ShouldReactWithHeart_WhenChatIsInLinkMode()
    {
        using var host = GetTelegramTestHost();

        await SendText(host, 1, "First note");
        await host.SendUpdateAsync(new Update
        {
            EditedMessage = new Message
            {
                From = DefaultUser,
                Id = 1,
                Text = "First note, edited",
                Chat = DefaultUserChat,
            }
        });

        // Only the new card got a reply - an edit is confirmed with a reaction, not another link.
        Assert.Single(host.Requests().OfType<SendMessageRequest>());

        var reaction = host.Requests().Single<SetMessageReactionRequest>();
        Assert.Equal("❤", Assert.IsType<ReactionTypeEmoji>(Assert.Single(reaction.Reaction!)).Emoji);
    }

    [Fact]
    public async Task SwitchToReactions_ShouldKeepLinkAndReact_WhenButtonTapped()
    {
        using var host = GetTelegramTestHost();

        await SendText(host, 1, "First note");
        var reply = host.Requests().Single<SendMessageRequest>();
        var rows = Assert.IsType<InlineKeyboardMarkup>(reply.ReplyMarkup).InlineKeyboard.ToList();

        await host.SendUpdateAsync(new Update
        {
            CallbackQuery = new CallbackQuery
            {
                Id = "1",
                From = DefaultUser,
                Message = new Message
                {
                    Id = 100,
                    Chat = DefaultUserChat,
                    ReplyMarkup = new InlineKeyboardMarkup(rows),
                },
                Data = Assert.Single(rows[1]).CallbackData,
            }
        });

        var editMarkup = host.Requests().Single<EditMessageReplyMarkupRequest>();
        Assert.Equal(100, editMarkup.MessageId);
        var keptRow = Assert.Single(editMarkup.ReplyMarkup!.InlineKeyboard);
        Assert.Equal("🔗 Open issue", Assert.Single(keptRow).Text);

        var reaction = host.Requests().Single<SetMessageReactionRequest>();
        Assert.Equal(1, reaction.MessageId);
        Assert.Equal("👍", Assert.IsType<ReactionTypeEmoji>(Assert.Single(reaction.Reaction!)).Emoji);

        // Telegram.NET also answers every callback on its own, so look for the one with the toast.
        Assert.Contains(
            host.Requests().OfType<AnswerCallbackQueryRequest>(),
            x => x.Text == "Done: from now on I'll just react with 👍 to saved messages.");

        var db = host.CreateScope().GetDatabaseContext();
        var linkedChat = Assert.Single(await db.LinkedTelegramChats.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal(ReplyMode.Reaction, linkedChat.ReplyMode);
    }

    [Fact]
    public async Task HandleTextMessage_ShouldReactOnly_WhenUserSwitchedToReactions()
    {
        using var host = GetTelegramTestHost();

        await SendText(host, 1, "First note");
        var rows = Assert.IsType<InlineKeyboardMarkup>(host.Requests().Single<SendMessageRequest>().ReplyMarkup)
            .InlineKeyboard
            .ToList();

        await host.SendUpdateAsync(new Update
        {
            CallbackQuery = new CallbackQuery
            {
                Id = "1",
                From = DefaultUser,
                Message = new Message { Id = 100, Chat = DefaultUserChat, ReplyMarkup = new InlineKeyboardMarkup(rows) },
                Data = Assert.Single(rows[1]).CallbackData,
            }
        });

        await SendText(host, 2, "Second note");

        // Still the one link reply from the first note; the second only gets a 👍.
        Assert.Single(host.Requests().OfType<SendMessageRequest>());
        var reaction = host.Requests().OfType<SetMessageReactionRequest>().Last();
        Assert.Equal(2, reaction.MessageId);
        Assert.Equal("👍", Assert.IsType<ReactionTypeEmoji>(Assert.Single(reaction.Reaction!)).Emoji);
    }

    [Fact]
    public async Task HandleTextMessage_ShouldReactOnly_WhenExistingChatKeepsDefaultReplyMode()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        // A chat linked before BRD-361: the column's default, Reaction.
        var userId = await testScope.CreateUser(x => x.TelegramId = DefaultUser.Id);
        var organization = await testScope.InitializeOrganization(userId);
        testScope.Database.Add(new LinkedTelegramChat
        {
            ExternalChatId = DefaultUser.Id,
            StatusId = organization.GetStatus(0, 0, 0).Id,
            OwnerId = userId,
            SaveMode = SaveMode.EachMessage,
            LinkedAt = DateTime.UtcNow,
        });
        await testScope.Database.SaveChangesAsync();

        await SendText(host, 1, "Old user note");

        Assert.Empty(host.Requests().OfType<SendMessageRequest>());
        var reaction = host.Requests().Single<SetMessageReactionRequest>();
        Assert.Equal("👍", Assert.IsType<ReactionTypeEmoji>(Assert.Single(reaction.Reaction!)).Emoji);
    }

    private static Task SendText(AppTelegramTestHost host, int messageId, string text)
    {
        return host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = DefaultUser,
                Id = messageId,
                Text = text,
                Chat = DefaultUserChat,
            }
        });
    }
}
