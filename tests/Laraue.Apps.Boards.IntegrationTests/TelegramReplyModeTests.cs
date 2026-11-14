using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Telegram.NET.Testing;
using LinqToDB.EntityFrameworkCore;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// BRD-361: a new user's personal chat answers a saved message with the card's link instead of a 👍; chats linked
/// before keep reactions.
/// </summary>
public class TelegramReplyModeTests : TelegramIntegrationTest
{
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
    public async Task HandleTextMessage_ShouldReplyWithLink_WhenChatIsInLinkMode()
    {
        using var host = GetTelegramTestHost();

        await SendText(host, 1, "First note");

        Assert.Empty(host.Requests().OfType<SetMessageReactionRequest>());

        var reply = host.Requests().Single<SendMessageRequest>();
        Assert.Equal(1, reply.ReplyParameters?.MessageId);
        Assert.Contains("First note", reply.Text);

        var row = Assert.Single(Assert.IsType<InlineKeyboardMarkup>(reply.ReplyMarkup).InlineKeyboard);
        var openButton = Assert.Single(row);
        Assert.Equal("🔗 Open issue", openButton.Text);
        Assert.NotNull(openButton.Url);
    }

    [Fact]
    public async Task HandleTextMessage_ShouldReplyWithLinkToEachNewCard_WhenChatIsInLinkMode()
    {
        using var host = GetTelegramTestHost();

        await SendText(host, 1, "First note");
        await SendText(host, 2, "Second note");

        Assert.Empty(host.Requests().OfType<SetMessageReactionRequest>());

        var replies = host.Requests().OfType<SendMessageRequest>().ToList();
        Assert.Equal([1, 2], replies.Select(x => x.ReplyParameters?.MessageId));
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
