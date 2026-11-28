using Laraue.Apps.Boards.DataAccess.Models;
using Moq;
using Microsoft.Extensions.DependencyInjection;
using Laraue.Apps.Boards.Services.Ai;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
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
/// BRD-365: a chat fixed only part of the way down asks where each message goes, and creates the card once picked.
/// </summary>
public class TelegramSaveDestinationTests : TelegramIntegrationTest
{
    private const int PickerMessageId = 100;

    private static Chat Group => new() { Id = 777, Type = ChatType.Group, Title = "Team" };

    [Fact]
    public async Task HandleTextMessage_ShouldAskWhereAndSaveToPickedEpic_WhenChatFixesOnlyTheSpace()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(x => x.TelegramId = AdminUser.Id);
        var organization = await testScope.InitializeOrganization(userId);
        var space = organization.GetSpace(0);
        var backlog = organization.GetEpic(0, 0);
        await LinkGroup(testScope, organization.Id, LinkDestinationType.Space, space.Id, userId);

        await host.SendUpdateAsync(new Update
        {
            Message = new Message { From = AdminUser, Id = 5, Text = "Buy milk", Chat = Group },
        });

        // Recorded, no card yet: the sender is asked for the epic.
        var db = host.CreateScope().GetDatabaseContext();
        Assert.Empty(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
        var telegramMessageId = Assert.Single(await db.TelegramMessages.AsNoTracking().ToListAsyncLinqToDB()).Id;

        var picker = host.Requests().Single<SendMessageRequest>();
        Assert.Equal(5, picker.ReplyParameters?.MessageId);
        Assert.StartsWith("📥 Where should this go?", picker.Text);
        var backlogButton = Assert.IsType<InlineKeyboardMarkup>(picker.ReplyMarkup).InlineKeyboard
            .SelectMany(row => row)
            .Single(x => x.Text == $"📥 {backlog.Name}");
        Assert.Equal($"/save/where/{telegramMessageId}/false/2/{backlog.Id}", backlogButton.CallbackData);

        // A backlog epic has a single status: picking it saves right away.
        await TapPicker(host, AdminUser, backlogButton.CallbackData!);

        var issue = Assert.Single(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal("Buy milk", issue.Title);
        Assert.Null(issue.Content);
        Assert.Equal(organization.GetStatus(0, 0, 0).Id, issue.StatusId);

        // Link mode: the picker itself becomes the card.
        var card = host.Requests().Single<EditMessageTextRequest>();
        Assert.Equal(PickerMessageId, card.MessageId);
        var openButton = Assert.Single(Assert.Single(Assert.IsType<InlineKeyboardMarkup>(card.ReplyMarkup).InlineKeyboard));
        Assert.Equal("🔗 Open issue", openButton.Text);
    }

    [Fact]
    public async Task HandlePicked_ShouldRefuse_WhenSomeoneElseTaps()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(x => x.TelegramId = AdminUser.Id);
        var organization = await testScope.InitializeOrganization(userId);
        var space = organization.GetSpace(0);
        await LinkGroup(testScope, organization.Id, LinkDestinationType.Space, space.Id, userId);

        await host.SendUpdateAsync(new Update
        {
            Message = new Message { From = AdminUser, Id = 5, Text = "Buy milk", Chat = Group },
        });

        var backlogButton = Assert.IsType<InlineKeyboardMarkup>(host.Requests().Single<SendMessageRequest>().ReplyMarkup)
            .InlineKeyboard
            .SelectMany(row => row)
            .First(x => x.CallbackData!.StartsWith("/save/where/"));

        await TapPicker(host, MemberUser, backlogButton.CallbackData!);

        Assert.Contains(
            host.Requests().OfType<AnswerCallbackQueryRequest>(),
            x => x.Text == "Only the person who saved this message can choose where it goes.");

        var db = host.CreateScope().GetDatabaseContext();
        Assert.Empty(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
    }

    [Fact]
    public async Task HandlePicked_ShouldWalkSpaceEpicStatus_WhenChatFixesOnlyTheOrganization()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(x => x.TelegramId = AdminUser.Id);
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o.AddSpace(userId, "AAA", s => s
                .AddEpic(userId, e => e.WithName("Sprint 1"))));
        var space = organization.GetSpace(1);
        var epic = organization.GetEpic(1, 1);
        var status = organization.GetStatus(1, 1, 0);
        await LinkGroup(testScope, organization.Id, LinkDestinationType.Organization, organization.Id, userId);

        await SendText(host, 5, "Plan the trip");
        var telegramMessageId = await GetTelegramMessageId(host, 5);

        // Spaces first, with no Back: the organization is what the chat fixes.
        var spaces = host.Requests().Single<SendMessageRequest>();
        Assert.Contains($"Choose a space in {organization.Name}:", spaces.Text);
        var spaceButtons = Buttons(spaces.ReplyMarkup);
        Assert.Contains(spaceButtons, x => x == ($"🗂️ {space.Name}", $"/save/where/{telegramMessageId}/false/1/{space.Id}"));
        Assert.DoesNotContain(spaceButtons, x => x.Text == "← Back");

        await TapPicker(host, AdminUser, $"/save/where/{telegramMessageId}/false/1/{space.Id}");
        var epics = host.Requests().OfType<EditMessageTextRequest>().Last();
        Assert.Contains($"Choose an epic in {organization.Name} → {space.Name}:", epics.Text);
        Assert.Contains(Buttons(epics.ReplyMarkup), x => x == ($"📋 {epic.Name}", $"/save/where/{telegramMessageId}/false/2/{epic.Id}"));
        Assert.Contains(Buttons(epics.ReplyMarkup), x => x == ("← Back", $"/save/where/{telegramMessageId}/false/0/{organization.Id}"));

        await TapPicker(host, AdminUser, $"/save/where/{telegramMessageId}/false/2/{epic.Id}");
        var statuses = host.Requests().OfType<EditMessageTextRequest>().Last();
        Assert.Contains($"Choose a status in {organization.Name} → {space.Name} → {epic.Name}:", statuses.Text);
        Assert.Contains(Buttons(statuses.ReplyMarkup), x => x == ($"🏷️ {status.Name}", $"/save/where/{telegramMessageId}/false/3/{status.Id}"));

        // Nothing is created until the last level is picked.
        var db = host.CreateScope().GetDatabaseContext();
        Assert.Empty(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());

        await TapPicker(host, AdminUser, $"/save/where/{telegramMessageId}/false/3/{status.Id}");

        var issue = Assert.Single(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal(status.Id, issue.StatusId);
        Assert.Equal("Plan the trip", issue.Title);
    }

    [Fact]
    public async Task HandlePicked_ShouldDeletePickerAndReact_WhenChatIsInReactionMode()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var (organization, userId) = await CreateOrganization(testScope);
        await LinkGroup(testScope, organization.Id, LinkDestinationType.Space, organization.GetSpace(0).Id, userId, replyMode: ReplyMode.Reaction);

        await SendText(host, 5, "Buy milk");
        await TapPicker(host, AdminUser, await BacklogCallback(host, organization, 5));

        var deleted = host.Requests().Single<DeleteMessageRequest>();
        Assert.Equal(PickerMessageId, deleted.MessageId);
        var reaction = host.Requests().Single<SetMessageReactionRequest>();
        Assert.Equal(5, reaction.MessageId);
        Assert.Equal("👍", Assert.IsType<ReactionTypeEmoji>(Assert.Single(reaction.Reaction!)).Emoji);
        Assert.Empty(host.Requests().OfType<EditMessageTextRequest>());
    }

    [Fact]
    public async Task HandlePicked_ShouldIncludeEdit_WhenMessageWasEditedWhilePickerWaited()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var (organization, userId) = await CreateOrganization(testScope);
        await LinkGroup(testScope, organization.Id, LinkDestinationType.Space, organization.GetSpace(0).Id, userId);

        await SendText(host, 5, "Buy milk");
        await host.SendUpdateAsync(new Update
        {
            EditedMessage = new Message { From = AdminUser, Id = 5, Text = "Buy oat milk", Chat = Group },
        });

        // The edit asks nothing new: the first picker still stands.
        Assert.Single(host.Requests().OfType<SendMessageRequest>());

        await TapPicker(host, AdminUser, await BacklogCallback(host, organization, 5));

        var db = host.CreateScope().GetDatabaseContext();
        Assert.Equal("Buy oat milk", Assert.Single(await db.Issues.AsNoTracking().ToListAsyncLinqToDB()).Title);
    }

    [Fact]
    public async Task HandlePicked_ShouldCreateOneCard_WhenTappedTwice()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var (organization, userId) = await CreateOrganization(testScope);
        await LinkGroup(testScope, organization.Id, LinkDestinationType.Space, organization.GetSpace(0).Id, userId);

        await SendText(host, 5, "Buy milk");
        var callback = await BacklogCallback(host, organization, 5);

        await TapPicker(host, AdminUser, callback);
        await TapPicker(host, AdminUser, callback);

        var db = host.CreateScope().GetDatabaseContext();
        Assert.Single(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
    }

    [Fact]
    public async Task HandleAlbum_ShouldSendOnePickerAndSaveWholeAlbum_WhenDestinationIsAsked()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var (organization, userId) = await CreateOrganization(testScope);
        await LinkGroup(testScope, organization.Id, LinkDestinationType.Space, organization.GetSpace(0).Id, userId);

        await SendPhoto(host, 5, caption: "Holiday");
        await SendPhoto(host, 6, caption: null);

        // One album, one picker - answering its first photo.
        var picker = host.Requests().Single<SendMessageRequest>();
        Assert.Equal(5, picker.ReplyParameters?.MessageId);

        await TapPicker(host, AdminUser, await BacklogCallback(host, organization, 5));

        var db = host.CreateScope().GetDatabaseContext();
        var issue = Assert.Single(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal("Holiday", issue.Title);
        Assert.Null(issue.Content);
        Assert.Equal(2, (await db.IssueAttachments.AsNoTracking().Where(x => x.IssueId == issue.Id).ToListAsyncLinqToDB()).Count);
    }

    [Fact]
    public async Task HandleSave_ShouldAskWhereAndKeepGivenTitle_WhenChatFixesOnlyTheSpace()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var (organization, userId) = await CreateOrganization(testScope);
        await LinkGroup(testScope, organization.Id, LinkDestinationType.Space, organization.GetSpace(0).Id, userId, SaveMode.BotMentionedMessages);

        await SendText(host, 5, "the login page throws 500");
        var saveCommand = new Message
        {
            From = AdminUser,
            Id = 6,
            Text = "/save Fix login",
            // Telegram tags a command's text; the picker recognises the /save it answers by that tag.
            Entities = [new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = 5 }],
            Chat = Group,
            ReplyToMessage = new Message { Id = 5, Chat = Group },
        };
        await host.SendUpdateAsync(new Update { Message = saveCommand });

        // The picker answers the /save command, which holds the title.
        var picker = host.Requests().Single<SendMessageRequest>();
        Assert.Equal(6, picker.ReplyParameters?.MessageId);

        var callback = await BacklogCallback(host, organization, 5);

        // Someone else can't pick for the /save they didn't send.
        await TapPicker(host, MemberUser, callback, replyTo: saveCommand);
        var db = host.CreateScope().GetDatabaseContext();
        Assert.Empty(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());

        await TapPicker(host, AdminUser, callback, replyTo: saveCommand);

        var issue = Assert.Single(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal("Fix login", issue.Title);
        Assert.Equal("the login page throws 500", issue.Content);
    }

    [Fact]
    public async Task HandlePicked_ShouldRefuse_WhenPickedDestinationIsOutsideTheChat()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var (organization, userId) = await CreateOrganization(testScope);
        var otherOrganization = await testScope.InitializeOrganization(userId);
        await LinkGroup(testScope, organization.Id, LinkDestinationType.Space, organization.GetSpace(0).Id, userId);

        await SendText(host, 5, "Buy milk");
        var telegramMessageId = await GetTelegramMessageId(host, 5);

        // Forged callback data: a status of an organization the chat isn't linked to.
        await TapPicker(host, AdminUser, $"/save/where/{telegramMessageId}/false/3/{otherOrganization.GetStatus(0, 0, 0).Id}");

        Assert.Contains(
            host.Requests().OfType<AnswerCallbackQueryRequest>(),
            x => x.ShowAlert == true);
        var db = host.CreateScope().GetDatabaseContext();
        Assert.Empty(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
    }

    [Fact]
    public async Task HandleTextMessage_ShouldListNoEpics_WhenSenderCannotReadTheChatsSpace()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser(x => x.TelegramId = AdminUser.Id);
        var memberId = await testScope.CreateUser(x => x.TelegramId = MemberUser.Id);
        // A member with no access to anything in the organization.
        var organization = await testScope.InitializeOrganization(ownerId, o => o.AddUser(memberId));
        await LinkGroup(testScope, organization.Id, LinkDestinationType.Space, organization.GetSpace(0).Id, ownerId);

        await host.SendUpdateAsync(new Update
        {
            Message = new Message { From = MemberUser, Id = 5, Text = "Buy milk", Chat = Group },
        });

        // The space's epics are readable only with the space: nothing to pick but Cancel.
        var picker = host.Requests().Single<SendMessageRequest>();
        Assert.Equal([("✖ Cancel", "/close-callback")], Buttons(picker.ReplyMarkup));
    }

    [Fact]
    public async Task HandlePicked_ShouldOnlyListAndOpenReadableSpaces_WhenChatFixesOnlyTheOrganization()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser(x => x.TelegramId = AdminUser.Id);
        var memberId = await testScope.CreateUser(x => x.TelegramId = MemberUser.Id);
        var organization = await testScope.InitializeOrganization(
            ownerId,
            o => o
                .AddSpace(ownerId, "AAA", s => s.AddEpic(ownerId, e => e.WithName("Secret plans")))
                .AddUser(memberId, b => b.SetSpaceAccessLevel(0, x => x.CanRead = true)));
        var readableSpace = organization.GetSpace(0);
        var hiddenSpace = organization.GetSpace(1);
        await LinkGroup(testScope, organization.Id, LinkDestinationType.Organization, organization.Id, ownerId);

        await host.SendUpdateAsync(new Update
        {
            Message = new Message { From = MemberUser, Id = 5, Text = "Buy milk", Chat = Group },
        });
        var telegramMessageId = await GetTelegramMessageId(host, 5);

        var spaces = Buttons(host.Requests().Single<SendMessageRequest>().ReplyMarkup);
        Assert.Contains(spaces, x => x.Text == $"🗂️ {readableSpace.Name}");
        Assert.DoesNotContain(spaces, x => x.Text == $"🗂️ {hiddenSpace.Name}");

        // Forged callback data opening the space the member can't read: refused, its epics never shown.
        await TapPicker(
            host,
            MemberUser,
            $"/save/where/{telegramMessageId}/false/1/{hiddenSpace.Id}",
            replyTo: new Message { Id = 5, Chat = Group, Text = "Buy milk" });

        Assert.Contains(
            host.Requests().OfType<AnswerCallbackQueryRequest>(),
            x => x.ShowAlert == true);
        Assert.Empty(host.Requests().OfType<EditMessageTextRequest>());
    }

    [Fact]
    public async Task HandleEditedMessage_ShouldAskWhere_WhenEditFirstGivesMessageSomethingToSave()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var (organization, userId) = await CreateOrganization(testScope);
        await LinkGroup(testScope, organization.Id, LinkDestinationType.Space, organization.GetSpace(0).Id, userId);

        // An unsupported file alone: nothing to save, so no picker - only the notice that the file wasn't saved.
        var installer = new Document
        {
            FileId = "installer",
            FileUniqueId = "installerUnique",
            FileName = "setup.exe",
            MimeType = "application/x-msdownload",
        };
        await host.SendUpdateAsync(new Update
        {
            Message = new Message { From = AdminUser, Id = 5, Document = installer, Chat = Group },
        });

        var notice = host.Requests().Single<SendMessageRequest>();
        Assert.DoesNotContain("Where should this go?", notice.Text);

        // The edit adds a caption: now there's something to save, so it's asked about like a new message.
        await host.SendUpdateAsync(new Update
        {
            EditedMessage = new Message { From = AdminUser, Id = 5, Document = installer, Caption = "Installer notes", Chat = Group },
        });

        var picker = host.Requests().OfType<SendMessageRequest>().Last();
        Assert.StartsWith("📥 Where should this go?", picker.Text);
        Assert.Equal(5, picker.ReplyParameters?.MessageId);

        // A further edit waits for the same picker.
        await host.SendUpdateAsync(new Update
        {
            EditedMessage = new Message { From = AdminUser, Id = 5, Document = installer, Caption = "Installer notes v2", Chat = Group },
        });
        Assert.Equal(2, host.Requests().OfType<SendMessageRequest>().Count());
    }

    [Fact]
    public async Task HandleAiSave_ShouldStillRewriteText_WhenCommandIsDeletedBeforeThePick()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();
        var (organization, userId) = await CreateOrganization(testScope);
        await LinkGroup(testScope, organization.Id, LinkDestinationType.Space, organization.GetSpace(0).Id, userId, SaveMode.BotMentionedMessages);

        await SendText(host, 5, "fix login bug, fails on retry");
        await host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = AdminUser,
                Id = 6,
                Text = "/aisave",
                Entities = [new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = 7 }],
                Chat = Group,
                ReplyToMessage = new Message { Id = 5, Chat = Group },
            }
        });

        // /aisave travels in the picker's buttons, not only in the command's text.
        var telegramMessageId = await GetTelegramMessageId(host, 5);
        var backlogCallback = $"/save/where/{telegramMessageId}/true/2/{organization.GetEpic(0, 0).Id}";
        Assert.Contains(Buttons(host.Requests().Single<SendMessageRequest>().ReplyMarkup), x => x.CallbackData == backlogCallback);

        var summarizer = host.CreateScope().ServiceProvider.GetRequiredService<IAiContentSummarizer>();
        Mock.Get(summarizer)
            .Setup(x => x.SummarizeAsync("fix login bug, fails on retry", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiSummarizationResult("Fix login bug", "- Login fails on retry", InputTokensCount: 10, OutputTokensCount: 20));

        // The command is deleted before the pick: the sender of the message picks, and the AI still runs.
        await TapPicker(host, AdminUser, backlogCallback, replyDeleted: true);

        var db = host.CreateScope().GetDatabaseContext();
        var issue = Assert.Single(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal("Fix login bug", issue.Title);
        Assert.Equal("- Login fails on retry", issue.Content);
    }

    /// <remarks>Takes the test's one scope: creating a test scope cleans the database.</remarks>
    private static async Task<(Organization Organization, Guid UserId)> CreateOrganization(
        AppTelegramTestHost.AppTelegramTestHostScope testScope)
    {
        var userId = await testScope.CreateUser(x => x.TelegramId = AdminUser.Id);

        return (await testScope.InitializeOrganization(userId), userId);
    }

    /// <summary>
    /// The picker's button for the default space's backlog, which saves right away.
    /// </summary>
    private static async Task<string> BacklogCallback(
        AppTelegramTestHost host,
        Organization organization,
        int externalMessageId)
    {
        return $"/save/where/{await GetTelegramMessageId(host, externalMessageId)}/false/2/{organization.GetEpic(0, 0).Id}";
    }

    private static async Task<long> GetTelegramMessageId(AppTelegramTestHost host, int externalMessageId)
    {
        var db = host.CreateScope().GetDatabaseContext();

        return (await db.TelegramMessages
            .AsNoTracking()
            .Where(x => x.ExternalMessageId == externalMessageId && x.ExternalChatId == Group.Id)
            .ToListAsyncLinqToDB())
            .Single()
            .Id;
    }

    private static List<(string Text, string? CallbackData)> Buttons(ReplyMarkup? markup)
    {
        return Assert.IsType<InlineKeyboardMarkup>(markup).InlineKeyboard
            .SelectMany(row => row)
            .Select(x => (x.Text, x.CallbackData))
            .ToList();
    }

    private static Task SendText(AppTelegramTestHost host, int messageId, string text)
    {
        return host.SendUpdateAsync(new Update
        {
            Message = new Message { From = AdminUser, Id = messageId, Text = text, Chat = Group },
        });
    }

    private static Task SendPhoto(AppTelegramTestHost host, int messageId, string? caption)
    {
        return host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = AdminUser,
                Id = messageId,
                Caption = caption,
                MediaGroupId = "album-1",
                Photo =
                [
                    new PhotoSize { FileId = $"preview{messageId}", FileUniqueId = $"previewUnique{messageId}" },
                    new PhotoSize { FileId = $"file{messageId}", FileUniqueId = $"fileUnique{messageId}" },
                ],
                Chat = Group,
            },
        });
    }

    /// <param name="replyTo">What the picker answers: the saved message in auto mode, the /save command otherwise.</param>
    /// <param name="replyDeleted">The message the picker answers was deleted: Telegram then sends no reply with the
    /// tap.</param>
    private static Task TapPicker(
        AppTelegramTestHost host,
        User user,
        string callbackData,
        Message? replyTo = null,
        bool replyDeleted = false)
    {
        return host.SendUpdateAsync(new Update
        {
            CallbackQuery = new CallbackQuery
            {
                Id = Guid.NewGuid().ToString(),
                From = user,
                Message = new Message
                {
                    Id = PickerMessageId,
                    Chat = Group,
                    ReplyToMessage = replyDeleted ? null : replyTo ?? new Message { Id = 5, Chat = Group, Text = "Buy milk" },
                },
                Data = callbackData,
            }
        });
    }

    private static async Task LinkGroup(
        AppTelegramTestHost.AppTelegramTestHostScope testScope,
        long organizationId,
        LinkDestinationType destinationType,
        long destinationId,
        Guid ownerId,
        SaveMode saveMode = SaveMode.EachMessage,
        ReplyMode replyMode = ReplyMode.IssueLink)
    {
        testScope.Database.Add(new LinkedTelegramChat
        {
            ExternalChatId = Group.Id,
            OrganizationId = organizationId,
            DestinationType = destinationType,
            DestinationId = destinationId,
            OwnerId = ownerId,
            SaveMode = saveMode,
            ReplyMode = replyMode,
            LinkedAt = DateTime.UtcNow,
        });
        await testScope.Database.SaveChangesAsync();
    }
}
