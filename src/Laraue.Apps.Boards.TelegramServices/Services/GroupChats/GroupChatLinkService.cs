using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices.Resources;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Telegram.NET.Core.Routing;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.Boards.TelegramServices.Services.GroupChats;

public interface IGroupChatLinkService
{
    Task HandleLinkCommand(
        Message message,
        Guid userId,
        CancellationToken cancellationToken);
    
    Task HandleUnlinkCommand(
        Message message,
        Guid userId,
        CancellationToken cancellationToken);
    
    Task HandleOrganizationSelected(
        CallbackQuery query,
        Guid userId,
        long organizationId,
        CancellationToken cancellationToken);
    
    Task HandleBackToOrganizations(
        CallbackQuery query,
        Guid userId,
        CancellationToken cancellationToken);
    
    Task HandleSpaceSelected(
        CallbackQuery query,
        Guid userId,
        long spaceId,
        CancellationToken cancellationToken);
    
    Task HandleEpicSelected(
        CallbackQuery query,
        Guid userId,
        long epicId,
        CancellationToken cancellationToken);
    
    Task HandleStatusSelected(
        CallbackQuery query,
        Guid userId,
        long statusId,
        CancellationToken cancellationToken);

    /// <summary>
    /// "Ask ... each time" in the space, epic or status picker: the destination ends at the given level, and the
    /// flow goes on to the save mode.
    /// </summary>
    Task HandleChooseEachTime(
        CallbackQuery query,
        Guid userId,
        LinkDestinationType destinationType,
        long destinationId,
        CancellationToken cancellationToken);

    Task HandleSaveModeSelected(
        CallbackQuery query,
        Guid userId,
        LinkDestinationType destinationType,
        long destinationId,
        SaveMode saveMode,
        CancellationToken cancellationToken);

    /// <summary>
    /// The last step of linking: the chat is linked with the chosen status, save mode and reply mode.
    /// </summary>
    Task HandleReplyModeSelected(
        CallbackQuery query,
        Guid userId,
        LinkDestinationType destinationType,
        long destinationId,
        SaveMode saveMode,
        ReplyMode replyMode,
        CancellationToken cancellationToken);

    Task HandleUnlink(
        CallbackQuery query,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// "Change destination" in the settings menu: the organization picker, as for a new link.
    /// </summary>
    Task HandleChangeDestination(
        CallbackQuery query,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sets a linked chat's save mode from the settings menu and redraws the menu.
    /// </summary>
    Task HandleSettingsSaveModeSelected(
        CallbackQuery query,
        Guid userId,
        SaveMode saveMode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sets a linked chat's reply mode from the settings menu and redraws the menu.
    /// </summary>
    Task HandleSettingsReplyModeSelected(
        CallbackQuery query,
        Guid userId,
        ReplyMode replyMode,
        CancellationToken cancellationToken);
}

public class GroupChatLinkService(
    IGroupChatAdminService chatAdminService,
    ITelegramBotClient client,
    DatabaseContext context,
    IAccessService accessService,
    IDateTimeProvider dateTimeProvider,
    ILinkDestinationReader destinationReader,
    IDestinationOptionsReader optionsReader,
    IChatSettingsDocs docs)
    : IGroupChatLinkService
{
    public async Task HandleLinkCommand(
        Message message,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        if (!await EnsureUserIsGroupAdmin(message, cancellationToken))
            return;

        await ShowLinkEntryPoint(chatId, userId, null, cancellationToken);
    }

    public async Task HandleUnlinkCommand(Message message, Guid userId, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        if (!await EnsureUserIsGroupAdmin(message, cancellationToken))
            return;
        
        var settings = await GetActiveSettings(chatId, cancellationToken);
        if (settings is null)
        {
            await client.SendMessage(chatId, Phrases.LinkNotLinked, cancellationToken: cancellationToken);
            return;
        }

        await context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == chatId && x.UnlinkedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.UnlinkedAt, dateTimeProvider.UtcNow),
                cancellationToken);

        await client.SendMessage(chatId, Phrases.LinkUnlinked, cancellationToken: cancellationToken);
    }

    public async Task HandleOrganizationSelected(
        CallbackQuery query,
        Guid userId,
        long organizationId,
        CancellationToken cancellationToken)
    {
        var chatId = query.Message!.Chat.Id;

        if (!await IsAllowedToLink(query, userId, organizationId, cancellationToken))
            return;

        var organization = await LoadOrAnswerNotFound(
            query,
            () => context.ActiveOrganizations()
                .Where(x => x.Id == organizationId)
                .Select(x => new { x.Name })
                .FirstOrDefaultAsyncEF(cancellationToken),
            cancellationToken);

        if (organization is null)
            return;

        var spaces = await optionsReader.GetOptionsBelow(
            LinkDestinationType.Organization,
            organizationId,
            organizationId,
            userId,
            cancellationToken);

        var allButtons = OptionButtons(spaces, TelegramRoutes.LinkSpace)
            .Append([ChooseEachTimeButton(LinkDestinationType.Organization, organizationId, Phrases.LinkAskSpaceEachTime)])
            .Append([docs.WhichToChooseButton(ChatSettingsDocs.Anchors.Destination)])
            .AddBackButton(new CallbackRoutePath(TelegramRoutes.BackToLink))
            .AddCancelButton();

        await client.EditMessageText(
            chatId,
            query.Message.MessageId,
            string.Format(Phrases.LinkChooseSpace, organization.Name),
            replyMarkup: new InlineKeyboardMarkup(allButtons),
            cancellationToken: cancellationToken);
    }

    public async Task HandleBackToOrganizations(CallbackQuery query, Guid userId, CancellationToken cancellationToken)
    {
        var chatId = query.Message!.Chat.Id;
        var editedMessageId = query.Message.MessageId;

        if (!await EnsureUserIsGroupAdmin(query, cancellationToken))
            return;

        await ShowLinkEntryPoint(chatId, userId, editedMessageId, cancellationToken);
    }

    public async Task HandleSpaceSelected(CallbackQuery query, Guid userId, long spaceId, CancellationToken cancellationToken)
    {
        var chatId = query.Message!.Chat.Id;

        var space = await LoadOrAnswerNotFound(
            query,
            () => context.ActiveSpaces()
                .Where(x => x.Id == spaceId)
                .Select(x => new
                {
                    x.OrganizationId,
                    x.Name,
                    OrganizationName = x.Organization!.Name
                })
                .SingleOrDefaultAsyncEF(cancellationToken),
            cancellationToken);

        if (space is null)
            return;

        if (!await IsAllowedToLink(query, userId, space.OrganizationId, cancellationToken))
            return;

        var epics = await optionsReader.GetOptionsBelow(
            LinkDestinationType.Space,
            spaceId,
            space.OrganizationId,
            userId,
            cancellationToken);

        var buttons = OptionButtons(epics, TelegramRoutes.LinkEpic)
            .Append([ChooseEachTimeButton(LinkDestinationType.Space, spaceId, Phrases.LinkAskEpicEachTime)])
            .Append([docs.WhichToChooseButton(ChatSettingsDocs.Anchors.Destination)])
            .AddBackButton(new CallbackRoutePath(TelegramRoutes.LinkOrganization)
                .WithPathParameter(TelegramRoutes.Parameters.Id, space.OrganizationId.ToString()))
            .AddCancelButton();

        await client.EditMessageText(
            chatId,
            query.Message.MessageId,
            string.Format(Phrases.LinkChooseEpic, $"{space.OrganizationName} → {space.Name}"),
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }

    public async Task HandleEpicSelected(
        CallbackQuery query,
        Guid userId,
        long epicId,
        CancellationToken cancellationToken)
    {
        var chatId = query.Message!.Chat.Id;
        var epic = await LoadOrAnswerNotFound(
            query,
            () => context.ActiveEpics()
                .Where(x => x.Id == epicId)
                .Select(x => new
                {
                    OrganizationName = x.Space!.Organization!.Name,
                    x.Space!.OrganizationId,
                    SpaceName = x.Space.Name,
                    SpaceId = x.Space.Id,
                    x.IsDefault,
                    x.Name,
                })
                .SingleOrDefaultAsyncEF(cancellationToken),
            cancellationToken);

        if (epic is null)
            return;

        if (!await IsAllowedToLink(query, userId, epic.OrganizationId, cancellationToken))
            return;

        // A backlog epic has a single status: it's linked to without asking for one.
        if (epic.IsDefault)
        {
            var backlogStatusId = await optionsReader.GetBacklogStatusId(
                epicId,
                epic.OrganizationId,
                userId,
                cancellationToken);
            if (backlogStatusId is null)
            {
                await AnswerItemNotFound(query, cancellationToken);
                return;
            }

            await ShowSaveModePicker(query, userId, LinkDestinationType.Status, backlogStatusId.Value, cancellationToken);

            return;
        }

        var statuses = await optionsReader.GetOptionsBelow(
            LinkDestinationType.Epic,
            epicId,
            epic.OrganizationId,
            userId,
            cancellationToken);

        var buttons = OptionButtons(statuses, TelegramRoutes.LinkStatus)
            .Append([ChooseEachTimeButton(LinkDestinationType.Epic, epicId, Phrases.LinkAskStatusEachTime)])
            .Append([docs.WhichToChooseButton(ChatSettingsDocs.Anchors.Destination)])
            .AddBackButton(new CallbackRoutePath(TelegramRoutes.LinkSpace)
                .WithPathParameter(TelegramRoutes.Parameters.Id, epic.SpaceId.ToString()))
            .AddCancelButton();
        
        await client.EditMessageText(
            chatId,
            query.Message.MessageId,
            string.Format(Phrases.LinkChooseStatus, $"{epic.OrganizationName} → {epic.SpaceName} → {epic.Name}"),
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }

    public Task HandleStatusSelected(
        CallbackQuery query,
        Guid userId,
        long statusId,
        CancellationToken cancellationToken)
    {
        return ShowSaveModePicker(query, userId, LinkDestinationType.Status, statusId, cancellationToken);
    }

    public Task HandleChooseEachTime(
        CallbackQuery query,
        Guid userId,
        LinkDestinationType destinationType,
        long destinationId,
        CancellationToken cancellationToken)
    {
        return ShowSaveModePicker(query, userId, destinationType, destinationId, cancellationToken);
    }

    public async Task HandleSaveModeSelected(
        CallbackQuery query,
        Guid userId,
        LinkDestinationType destinationType,
        long destinationId,
        SaveMode saveMode,
        CancellationToken cancellationToken)
    {
        var destination = await LoadDestinationToLink(query, userId, destinationType, destinationId, cancellationToken);
        if (destination is null)
            return;

        await SendReplyModePicker(query.Message!.Chat.Id, query.Message.MessageId, destination, saveMode, cancellationToken);
    }

    public async Task HandleReplyModeSelected(
        CallbackQuery query,
        Guid userId,
        LinkDestinationType destinationType,
        long destinationId,
        SaveMode saveMode,
        ReplyMode replyMode,
        CancellationToken cancellationToken)
    {
        var destination = await LoadDestinationToLink(query, userId, destinationType, destinationId, cancellationToken);
        if (destination is null)
            return;

        var message = query.Message!;
        await LinkChat(
            message.Chat.Id,
            message.Chat.Title,
            message.Chat.Username,
            destination,
            userId,
            saveMode,
            replyMode,
            cancellationToken);

        await SendLinkConfirmed(message.Chat.Id, message.MessageId, destination, saveMode, cancellationToken);
    }

    private async Task ShowSaveModePicker(
        CallbackQuery query,
        Guid userId,
        LinkDestinationType destinationType,
        long destinationId,
        CancellationToken cancellationToken)
    {
        var destination = await LoadDestinationToLink(query, userId, destinationType, destinationId, cancellationToken);
        if (destination is null)
            return;

        await SendSaveModePicker(query.Message!.Chat.Id, query.Message.MessageId, destination, cancellationToken);
    }

    /// <summary>
    /// The destination a step of the link flow carries, if it still exists and the user may link chats to its
    /// organization. Otherwise the callback is answered (not found / not allowed) and null is returned.
    /// </summary>
    private async Task<LinkDestination?> LoadDestinationToLink(
        CallbackQuery query,
        Guid userId,
        LinkDestinationType destinationType,
        long destinationId,
        CancellationToken cancellationToken)
    {
        var destination = await LoadOrAnswerNotFound(
            query,
            () => destinationReader.Load(destinationType, destinationId, includeDeleted: false, cancellationToken),
            cancellationToken);

        if (destination is null)
            return null;

        if (!await IsAllowedToLink(query, userId, destination.OrganizationId, cancellationToken))
            return null;

        // Linking chats in an organization doesn't by itself let a chat save into a space the user can't read.
        if (!await optionsReader.CanRead(destination, userId, cancellationToken))
        {
            await AnswerItemNotFound(query, cancellationToken);
            return null;
        }

        return destination;
    }

    /// <summary>
    /// One row per option, each opening <paramref name="route"/> for it - the next step of /link.
    /// </summary>
    private static IEnumerable<InlineKeyboardButton[]> OptionButtons(IEnumerable<DestinationOption> options, string route)
    {
        return options.Select(option => new[]
        {
            new CallbackRoutePath(route)
                .WithPathParameter(TelegramRoutes.Parameters.Id, option.Id.ToString())
                .ToInlineKeyboardButton(option.Label),
        });
    }

    private static InlineKeyboardButton ChooseEachTimeButton(LinkDestinationType type, long id, string text)
    {
        return new CallbackRoutePath(TelegramRoutes.LinkChooseEachTime)
            .WithPathParameter(TelegramRoutes.Parameters.DestinationType, ((int)type).ToString())
            .WithPathParameter(TelegramRoutes.Parameters.DestinationId, id.ToString())
            .ToInlineKeyboardButton(text);
    }

    public async Task HandleUnlink(CallbackQuery query, Guid userId, CancellationToken cancellationToken)
    {
        var chatId = query.Message!.Chat.Id;
        var linkedChat = await LoadOrAnswerNotFound(
            query,
            () => context.LinkedTelegramChats
                .Where(x => x.ExternalChatId == chatId && x.UnlinkedAt == null)
                .Select(x => new { x.OrganizationId })
                .FirstOrDefaultAsyncEF(cancellationToken),
            cancellationToken);

        if (linkedChat is null)
            return;

        // IsAllowedToLink already answers the callback query (admin/access error) before returning false.
        if (!await IsAllowedToLink(query, userId, linkedChat.OrganizationId, cancellationToken))
            return;

        await context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == chatId && x.UnlinkedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.UnlinkedAt, dateTimeProvider.UtcNow),
                cancellationToken);

        await client.EditMessageText(
            chatId,
            query.Message.MessageId,
            Phrases.LinkUnlinked,
            cancellationToken: cancellationToken);
    }

    public async Task HandleChangeDestination(CallbackQuery query, Guid userId, CancellationToken cancellationToken)
    {
        var chatId = query.Message!.Chat.Id;

        if (!await EnsureUserIsGroupAdmin(query, cancellationToken))
            return;

        await SendOrganizationPicker(chatId, userId, query.Message.MessageId, cancellationToken);
    }

    public Task HandleSettingsSaveModeSelected(
        CallbackQuery query,
        Guid userId,
        SaveMode saveMode,
        CancellationToken cancellationToken)
    {
        return UpdateSettings(
            query,
            userId,
            setters => setters.SetProperty(x => x.SaveMode, saveMode),
            cancellationToken);
    }

    public Task HandleSettingsReplyModeSelected(
        CallbackQuery query,
        Guid userId,
        ReplyMode replyMode,
        CancellationToken cancellationToken)
    {
        return UpdateSettings(
            query,
            userId,
            setters => setters.SetProperty(x => x.ReplyMode, replyMode),
            cancellationToken);
    }

    /// <summary>
    /// Applies one settings-menu change to the chat's active link, then redraws the menu with the new values. The
    /// buttons carry the value to set, not "toggle", so a repeated tap leaves it as chosen.
    /// </summary>
    private async Task UpdateSettings(
        CallbackQuery query,
        Guid userId,
        Action<UpdateSettersBuilder<LinkedTelegramChat>> setters,
        CancellationToken cancellationToken)
    {
        var chatId = query.Message!.Chat.Id;
        var settings = await LoadOrAnswerNotFound(
            query,
            () => GetActiveSettings(chatId, cancellationToken),
            cancellationToken);

        if (settings is null)
            return;

        if (!await IsAllowedToLink(query, userId, settings.Destination.OrganizationId, cancellationToken))
            return;

        await context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == chatId && x.UnlinkedAt == null)
            .ExecuteUpdateAsync(setters, cancellationToken);

        var updated = await GetActiveSettings(chatId, cancellationToken);
        if (updated is not null)
            await SendSettingsMenu(chatId, updated, query.Message.MessageId, cancellationToken);
    }

    private async Task LinkChat(
        long externalChatId,
        string? chatTitle,
        string? chatUsername,
        LinkDestination destination,
        Guid userId,
        SaveMode saveMode,
        ReplyMode replyMode,
        CancellationToken cancellationToken)
    {
        var chat = await context.LinkedTelegramChats
            .FirstOrDefaultAsyncEF(x => x.ExternalChatId == externalChatId, cancellationToken);

        if (chat is null)
        {
            chat = new LinkedTelegramChat { ExternalChatId = externalChatId };
            context.Add(chat);
        }

        chat.Title = chatTitle;
        chat.Username = chatUsername;
        chat.OrganizationId = destination.OrganizationId;
        chat.DestinationType = destination.Type;
        chat.DestinationId = destination.Id;
        chat.OwnerId = userId;
        chat.SaveMode = saveMode;
        chat.ReplyMode = replyMode;
        chat.LinkedAt = dateTimeProvider.UtcNow;
        chat.UnlinkedAt = null;

        await context.SaveChangesAsync(cancellationToken);
    }

    private Task SendLinkConfirmed(
        ChatId chatId,
        int messageId,
        LinkDestination destination,
        SaveMode saveMode,
        CancellationToken cancellationToken)
    {
        var instructions = saveMode switch
        {
            SaveMode.EachMessage => Phrases.SaveModeInstructionsEachMessage,
            SaveMode.BotMentionedMessages => Phrases.SaveModeInstructionsBotMentioned,
            _ => throw new ArgumentOutOfRangeException(nameof(saveMode), saveMode, null),
        };

        return client.EditMessageText(
            chatId,
            messageId,
            string.Format(Phrases.LinkConfirmed, destination, instructions),
            cancellationToken: cancellationToken);
    }

    private Task SendReplyModePicker(
        ChatId chatId,
        int messageId,
        LinkDestination destination,
        SaveMode saveMode,
        CancellationToken cancellationToken)
    {
        InlineKeyboardButton ReplyModeButton(ReplyMode replyMode, string text) =>
            new CallbackRoutePath(TelegramRoutes.LinkReplyMode)
                .WithPathParameter(TelegramRoutes.Parameters.DestinationType, ((int)destination.Type).ToString())
                .WithPathParameter(TelegramRoutes.Parameters.DestinationId, destination.Id.ToString())
                .WithPathParameter(TelegramRoutes.Parameters.SaveMode, ((int)saveMode).ToString())
                .WithPathParameter(TelegramRoutes.Parameters.ReplyMode, ((int)replyMode).ToString())
                .ToInlineKeyboardButton(text);

        var buttons = new[]
        {
            new[] { ReplyModeButton(ReplyMode.IssueLink, Phrases.ReplyModeIssueLink) },
            new[] { ReplyModeButton(ReplyMode.Reaction, Phrases.ReplyModeReaction) },
            new[] { docs.WhichToChooseButton(ChatSettingsDocs.Anchors.ReplyMode) },
        }.AddCancelButton();

        return client.EditMessageText(
            chatId,
            messageId,
            Phrases.LinkChooseReplyMode,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }

    private Task SendSaveModePicker(
        ChatId chatId,
        int messageId,
        LinkDestination destination,
        CancellationToken cancellationToken)
    {
        // Path parameters are bound via JSON deserialization, so enums travel as their numeric values - an unquoted
        // enum name isn't valid JSON.
        InlineKeyboardButton SaveModeButton(SaveMode saveMode, string text) =>
            new CallbackRoutePath(TelegramRoutes.LinkSaveMode)
                .WithPathParameter(TelegramRoutes.Parameters.DestinationType, ((int)destination.Type).ToString())
                .WithPathParameter(TelegramRoutes.Parameters.DestinationId, destination.Id.ToString())
                .WithPathParameter(TelegramRoutes.Parameters.Mode, ((int)saveMode).ToString())
                .ToInlineKeyboardButton(text);

        var buttons = new[]
        {
            new[] { SaveModeButton(SaveMode.EachMessage, Phrases.SaveModeEachMessage) },
            new[] { SaveModeButton(SaveMode.BotMentionedMessages, Phrases.SaveModeBotMentioned) },
            new[] { docs.WhichToChooseButton(ChatSettingsDocs.Anchors.SaveMode) },
        }.AddCancelButton();

        return client.EditMessageText(
            chatId,
            messageId,
            Phrases.LinkChooseSaveMode,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }

    private async Task<LinkedChatSettings?> GetActiveSettings(long chatId, CancellationToken cancellationToken)
    {
        var chat = await context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == chatId && x.UnlinkedAt == null)
            .Select(x => new { x.DestinationType, x.DestinationId, x.SaveMode, x.ReplyMode })
            .SingleOrDefaultAsyncEF(cancellationToken);

        if (chat is null)
            return null;

        // A deleted destination still shows here (names included), so the chat can be relinked or unlinked.
        var destination = await destinationReader.Load(
            chat.DestinationType,
            chat.DestinationId,
            includeDeleted: true,
            cancellationToken);

        if (destination is null)
            return null;

        return new LinkedChatSettings
        {
            Destination = destination,
            SaveMode = chat.SaveMode,
            ReplyMode = chat.ReplyMode,
        };
    }

    // In a private chat the user is talking to the bot 1-on-1, so there is no separate "chat
    // admin" concept to check - the user always controls their own chat.
    private async Task<bool> EnsureUserIsGroupAdmin(
        Message message,
        CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var userTelegramId = message.From!.Id;

        if (message.Chat.Type == ChatType.Private)
            return true;

        if (await chatAdminService.IsAdmin(chatId, userTelegramId, cancellationToken))
            return true;

        await client.SendMessage(
            chatId,
            Phrases.LinkRequireAdmin,
            cancellationToken: cancellationToken);

        return false;
    }

    private async Task<bool> EnsureUserIsGroupAdmin(
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message!.Chat.Id;
        var userTelegramId = callbackQuery.From.Id;

        if (callbackQuery.Message.Chat.Type == ChatType.Private)
            return true;

        if (await chatAdminService.IsAdmin(chatId, userTelegramId, cancellationToken))
            return true;

        await client.AnswerCallbackQuery(
            callbackQuery.Id,
            Phrases.LinkRequireAdmin,
            cancellationToken: cancellationToken);

        return false;
    }

    private async Task<bool> EnsureUserCanLinkOrganization(
        Guid userId,
        long organizationId,
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        if (await CanLinkToOrganization(userId, organizationId, cancellationToken))
            return true;
        
        await client.AnswerCallbackQuery(
            callbackQuery.Id,
            Phrases.LinkRequireAdmin,
            cancellationToken: cancellationToken);
            
        return false;
    }

    private Task AnswerItemNotFound(CallbackQuery query, CancellationToken cancellationToken)
    {
        return client.AnswerCallbackQuery(
            query.Id,
            Phrases.LinkItemNotFound,
            showAlert: true,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="loader"/> and, if it yields nothing (e.g. the entity was deleted
    /// between rendering the picker and the user tapping the button), answers the callback
    /// query with a "not found" notice instead of letting the caller crash on a missing row.
    /// </summary>
    private async Task<T?> LoadOrAnswerNotFound<T>(
        CallbackQuery query,
        Func<Task<T?>> loader,
        CancellationToken cancellationToken)
        where T : class
    {
        var result = await loader();

        if (result is null)
            await AnswerItemNotFound(query, cancellationToken);

        return result;
    }

    private async Task<bool> IsAllowedToLink(
        CallbackQuery query,
        Guid userId,
        long organizationId,
        CancellationToken cancellationToken)
    {
        if (!await EnsureUserIsGroupAdmin(query, cancellationToken))
            return false;

        return await EnsureUserCanLinkOrganization(userId, organizationId, query, cancellationToken);
    }

    /// <summary>
    /// Shows the "already linked" menu if the chat is linked, otherwise the organization
    /// picker. Shared entry point for /link and the "Back" callback.
    /// </summary>
    private async Task ShowLinkEntryPoint(
        long chatId,
        Guid userId,
        int? editMessageId,
        CancellationToken cancellationToken)
    {
        var settings = await GetActiveSettings(chatId, cancellationToken);
        if (settings is not null)
        {
            await SendSettingsMenu(chatId, settings, editMessageId, cancellationToken);
            return;
        }

        await SendOrganizationPicker(chatId, userId, editMessageId, cancellationToken);
    }

    private async Task SendOrganizationPicker(
        ChatId chatId,
        Guid userId,
        int? editMessageId,
        CancellationToken cancellationToken)
    {
        var organizations = await GetLinkableOrganizationsQuery(userId, cancellationToken);

        string text;
        InlineKeyboardMarkup? markup;

        if (organizations.Count == 0)
        {
            var bot = await client.GetMe(cancellationToken);
            text = string.Format(Phrases.LinkNoOrganizations, bot.Username);
            markup = null;
        }
        else
        {
            text = Phrases.LinkChooseOrganization;

            var organizationButtons = organizations
                .Select(org => new[]
                {
                    new CallbackRoutePath(TelegramRoutes.LinkOrganization)
                        .WithPathParameter(TelegramRoutes.Parameters.Id, org.Id.ToString())
                        .ToInlineKeyboardButton($"🏢 {org.Name}")
                });

            var allButtons = organizationButtons.AddCancelButton();
            markup = new InlineKeyboardMarkup(allButtons);
        }

        await SendOrEdit(chatId, editMessageId, text, markup, cancellationToken);
    }

    /// <summary>
    /// /link in a linked chat: where it saves and how, with a button per setting showing the value it would switch
    /// to.
    /// </summary>
    private Task SendSettingsMenu(
        ChatId chatId,
        LinkedChatSettings settings,
        int? editMessageId,
        CancellationToken cancellationToken)
    {
        // Path parameters are bound via JSON deserialization, so modes travel as their numeric values.
        var otherSaveMode = settings.SaveMode == SaveMode.EachMessage
            ? SaveMode.BotMentionedMessages
            : SaveMode.EachMessage;
        var otherReplyMode = settings.ReplyMode == ReplyMode.IssueLink
            ? ReplyMode.Reaction
            : ReplyMode.IssueLink;

        var buttons = new[]
        {
            new[]
            {
                new CallbackRoutePath(TelegramRoutes.LinkSettingsSaveMode)
                    .WithPathParameter(TelegramRoutes.Parameters.Mode, ((int)otherSaveMode).ToString())
                    .ToInlineKeyboardButton(otherSaveMode == SaveMode.EachMessage
                        ? Phrases.SettingsSwitchToEachMessage
                        : Phrases.SettingsSwitchToBotMentioned),
            },
            new[]
            {
                new CallbackRoutePath(TelegramRoutes.LinkSettingsReplyMode)
                    .WithPathParameter(TelegramRoutes.Parameters.Mode, ((int)otherReplyMode).ToString())
                    .ToInlineKeyboardButton(otherReplyMode == ReplyMode.IssueLink
                        ? Phrases.SettingsSwitchToIssueLink
                        : Phrases.SettingsSwitchToReaction),
            },
            new[]
            {
                new CallbackRoutePath(TelegramRoutes.LinkChangeDestination)
                    .ToInlineKeyboardButton(Phrases.SettingsChangeDestination),
            },
            // Which settings fit which chat: the use cases.
            new[] { docs.WhichToChooseButton(ChatSettingsDocs.Anchors.UseCases) },
            new[] { new CallbackRoutePath(TelegramRoutes.Unlink).ToInlineKeyboardButton(Phrases.LinkUnlink) },
        }.AddCancelButton();

        var text = string.Format(
            Phrases.LinkSettings,
            settings.Destination,
            settings.SaveMode == SaveMode.EachMessage
                ? Phrases.SettingsSaveModeEachMessage
                : Phrases.SettingsSaveModeBotMentioned,
            settings.ReplyMode == ReplyMode.IssueLink
                ? Phrases.SettingsReplyModeIssueLink
                : Phrases.SettingsReplyModeReaction);

        return SendOrEdit(chatId, editMessageId, text, new InlineKeyboardMarkup(buttons), cancellationToken);
    }

    /// <summary>
    /// Edits <paramref name="editMessageId"/> in place if given, otherwise sends a new message.
    /// Shared by every picker/menu step so each one only builds its text and keyboard.
    /// </summary>
    private Task SendOrEdit(
        ChatId chatId,
        int? editMessageId,
        string text,
        InlineKeyboardMarkup? markup,
        CancellationToken cancellationToken)
    {
        if (editMessageId is not null)
        {
            return client.EditMessageText(
                chatId,
                editMessageId.Value,
                text,
                replyMarkup: markup,
                cancellationToken: cancellationToken);
        }

        return client.SendMessage(
            chatId,
            text,
            replyMarkup: markup,
            cancellationToken: cancellationToken);
    }
    
    private Task<List<OrganizationOption>> GetLinkableOrganizationsQuery(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return accessService.GetOrganizations(
            userId,
            query => query
                .Where(x => x.AdminAccessLevel.HasFlag(AdminAccessLevel.LinkChats))
                .Select(o => new OrganizationOption(o.OrganizationId, o.Organization!.Name))
                .ToListAsyncEF(cancellationToken));
    }

    private Task<bool> CanLinkToOrganization(Guid userId, long organizationId, CancellationToken cancellationToken)
    {
        return accessService.GetOrganizations(
            userId,
            query => query
                .Where(x => x.AdminAccessLevel.HasFlag(AdminAccessLevel.LinkChats))
                .AnyAsyncEF(x => x.OrganizationId == organizationId, cancellationToken));
    }
    
    private record OrganizationOption(long Id, string Name);

    private record LinkedChatSettings
    {
        public required LinkDestination Destination { get; init; }
        public required SaveMode SaveMode { get; init; }
        public required ReplyMode ReplyMode { get; init; }
    }
}
