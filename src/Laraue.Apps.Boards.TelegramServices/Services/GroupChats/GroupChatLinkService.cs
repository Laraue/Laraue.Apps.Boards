using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices.Resources;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Telegram.NET.Core.Routing;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
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

    Task HandleSaveModeSelected(
        CallbackQuery query,
        Guid userId,
        long statusId,
        SaveMode saveMode,
        CancellationToken cancellationToken);

    Task HandleUnlink(
        CallbackQuery query,
        Guid userId,
        CancellationToken cancellationToken);
}

public class GroupChatLinkService(
    IGroupChatAdminService chatAdminService,
    ITelegramBotClient client,
    DatabaseContext context,
    IOrganizationAccessService organizationAccessService,
    IAccessService accessService,
    IDateTimeProvider dateTimeProvider)
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
        
        var destination = await GetActiveDestination(chatId, cancellationToken);
        if (destination is null)
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
            () => context.Organizations
                .Where(x => x.Id == organizationId)
                .Select(x => new { x.Name })
                .FirstOrDefaultAsyncEF(cancellationToken),
            cancellationToken);

        if (organization is null)
            return;

        var spaces = await accessService.GetAvailableSpaces(
            new OrganizationAuthData { UserId = userId, OrganizationId = organizationId },
            x => x
                .Select(y => new { y.Id, y.Name })
                .ToListAsyncEF(cancellationToken),
            cancellationToken);

        var spaceButtons = spaces
            .Select(space => new[]
            {
                new CallbackRoutePath(TelegramRoutes.LinkSpace)
                    .WithPathParameter("id", space.Id.ToString())
                    .ToInlineKeyboardButton($"🗂️ {space.Name}")
            });

        var allButtons = spaceButtons
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
            () => context.Spaces
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
        
        var epics = await context.Epics
            .Where(x => x.SpaceId == spaceId)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Name, x.IsDefault })
            .ToListAsyncEF(cancellationToken);

        var buttons = epics
            .Select(e =>
            {
                var epicIcon = e.IsDefault ? "📥" : "📋";
                
                return new[]
                {
                    new CallbackRoutePath(TelegramRoutes.LinkEpic)
                        .WithPathParameter("id", e.Id.ToString())
                        .ToInlineKeyboardButton($"{epicIcon} {e.Name}")
                };
            })
            .AddBackButton(new CallbackRoutePath(TelegramRoutes.LinkOrganization)
                .WithPathParameter("id", space.OrganizationId.ToString()))
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
            () => context.Epics
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

        var statuses = await context.Statuses
            .Where(x => x.EpicId == epicId)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Name })
            .ToListAsyncEF(cancellationToken);

        // Handle backlog case
        if (epic.IsDefault)
        {
            var status = await LoadOrAnswerNotFound(
                query,
                () => Task.FromResult(statuses.FirstOrDefault()),
                cancellationToken);

            if (status is null)
                return;

            await SendSaveModePicker(
                chatId,
                query.Message.MessageId,
                status.Id,
                $"{epic.OrganizationName} → {epic.SpaceName} → {epic.Name}",
                cancellationToken);

            return;
        }
        
        var buttons = statuses
            .Select(status => new[]
            {
                new CallbackRoutePath(TelegramRoutes.LinkStatus)
                    .WithPathParameter("id", status.Id.ToString())
                    .ToInlineKeyboardButton($"🏷️ {status.Name}")
            })
            .AddBackButton(new CallbackRoutePath(TelegramRoutes.LinkSpace)
                .WithPathParameter("id", epic.SpaceId.ToString()))
            .AddCancelButton();
        
        await client.EditMessageText(
            chatId,
            query.Message.MessageId,
            string.Format(Phrases.LinkChooseStatus, $"{epic.OrganizationName} → {epic.SpaceName} -> {epic.Name}"),
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }

    public async Task HandleStatusSelected(
        CallbackQuery query,
        Guid userId,
        long statusId,
        CancellationToken cancellationToken)
    {
        var chatId = query.Message!.Chat.Id;
        var destination = await LoadOrAnswerNotFound(
            query,
            () => LoadDestinationByStatusId(statusId, cancellationToken),
            cancellationToken);

        if (destination is null)
            return;

        if (!await IsAllowedToLink(query, userId, destination.OrganizationId, cancellationToken))
            return;

        await SendSaveModePicker(
            chatId,
            query.Message.MessageId,
            destination.StatusId,
            $"{destination.OrganizationName} → {destination.SpaceName} → {destination.EpicName}",
            cancellationToken);
    }

    public async Task HandleSaveModeSelected(
        CallbackQuery query,
        Guid userId,
        long statusId,
        SaveMode saveMode,
        CancellationToken cancellationToken)
    {
        var chatId = query.Message!.Chat.Id;
        var destination = await LoadOrAnswerNotFound(
            query,
            () => LoadDestinationByStatusId(statusId, cancellationToken),
            cancellationToken);

        if (destination is null)
            return;

        if (!await IsAllowedToLink(query, userId, destination.OrganizationId, cancellationToken))
            return;

        await LinkToStatus(
            chatId,
            query.Message.Chat.Title,
            destination.StatusId,
            userId,
            saveMode,
            cancellationToken);

        await SendLinkConfirmed(chatId, query.Message.MessageId, destination, saveMode, cancellationToken);
    }

    public async Task HandleUnlink(CallbackQuery query, Guid userId, CancellationToken cancellationToken)
    {
        var chatId = query.Message!.Chat.Id;
        var linkedChat = await LoadOrAnswerNotFound(
            query,
            () => context.LinkedTelegramChats
                .Where(x => x.ExternalChatId == chatId && x.UnlinkedAt == null)
                .Select(x => new { x.Status!.Epic!.Space!.OrganizationId })
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

    private async Task LinkToStatus(
        long externalChatId,
        string? chatTitle,
        long statusId,
        Guid userId,
        SaveMode saveMode,
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
        chat.StatusId = statusId;
        chat.OwnerId = userId;
        chat.SaveMode = saveMode;
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

    private Task SendSaveModePicker(
        ChatId chatId,
        int messageId,
        long statusId,
        string destination,
        CancellationToken cancellationToken)
    {
        // Path parameters are bound via JSON deserialization, so the mode has to travel as its
        // numeric underlying value — an unquoted enum name isn't valid JSON.
        var buttons = new[]
        {
            new[]
            {
                new CallbackRoutePath(TelegramRoutes.LinkSaveMode)
                    .WithPathParameter("statusId", statusId.ToString())
                    .WithPathParameter("mode", ((int)SaveMode.EachMessage).ToString())
                    .ToInlineKeyboardButton(Phrases.SaveModeEachMessage)
            },
            new[]
            {
                new CallbackRoutePath(TelegramRoutes.LinkSaveMode)
                    .WithPathParameter("statusId", statusId.ToString())
                    .WithPathParameter("mode", ((int)SaveMode.BotMentionedMessages).ToString())
                    .ToInlineKeyboardButton(Phrases.SaveModeBotMentioned)
            },
        }.AddCancelButton();

        return client.EditMessageText(
            chatId,
            messageId,
            string.Format(Phrases.LinkChooseSaveMode, destination),
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }

    private Task<LinkDestination?> LoadDestinationByStatusId(long statusId, CancellationToken cancellationToken)
    {
        return context.Statuses
            .Where(x => x.Id == statusId)
            .Select(x => new LinkDestination
            {
                OrganizationId = x.Epic!.Space!.OrganizationId,
                StatusId = x.Id,
                OrganizationName = x.Epic.Space.Organization!.Name,
                SpaceName = x.Epic.Space.Name,
                EpicName = x.Epic.Name,
                StatusName = x.Epic.IsDefault ? null : x.Name,
            })
            .SingleOrDefaultAsyncEF(cancellationToken);
    }

    private Task<LinkDestination?> GetActiveDestination(long chatId, CancellationToken cancellationToken)
    {
        return context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == chatId && x.UnlinkedAt == null)
            .Select(x => new LinkDestination
            {
                OrganizationId = x.Status!.Epic!.Space!.OrganizationId,
                StatusId = x.Status.Id,
                EpicName = x.Status.Epic.Name,
                StatusName = x.Status.Epic.IsDefault ? null : x.Status.Name,
                SpaceName = x.Status.Epic.Space!.Name,
                OrganizationName = x.Status.Epic.Space!.Organization!.Name,
            })
            .SingleOrDefaultAsyncEF(cancellationToken);
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
        var destination = await GetActiveDestination(chatId, cancellationToken);
        if (destination is not null)
        {
            await SendAlreadyLinkedMenu(chatId, destination, editMessageId, cancellationToken);
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
                        .WithPathParameter("id", org.Id.ToString())
                        .ToInlineKeyboardButton($"🏢 {org.Name}")
                });

            var allButtons = organizationButtons.AddCancelButton();
            markup = new InlineKeyboardMarkup(allButtons);
        }

        await SendOrEdit(chatId, editMessageId, text, markup, cancellationToken);
    }

    private Task SendAlreadyLinkedMenu(
        ChatId chatId,
        LinkDestination destination,
        int? editMessageId,
        CancellationToken cancellationToken)
    {
        var buttons = new []
        {
            new [] { new CallbackRoutePath(TelegramRoutes.Unlink).ToInlineKeyboardButton(Phrases.LinkUnlink) }
        }.AddCancelButton();

        var markup = new InlineKeyboardMarkup(buttons);

        var text = string.Format(Phrases.LinkAlreadyLinked, destination);

        return SendOrEdit(chatId, editMessageId, text, markup, cancellationToken);
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
        return organizationAccessService.GetOrganizations(
            userId,
            query => query
                .Where(x => x.AdminAccessLevel.HasFlag(AdminAccessLevel.LinkChats))
                .Select(o => new OrganizationOption(o.OrganizationId, o.Organization!.Name))
                .ToListAsyncEF(cancellationToken));
    }

    private Task<bool> CanLinkToOrganization(Guid userId, long organizationId, CancellationToken cancellationToken)
    {
        return organizationAccessService.GetOrganizations(
            userId,
            query => query
                .Where(x => x.AdminAccessLevel.HasFlag(AdminAccessLevel.LinkChats))
                .AnyAsyncEF(x => x.OrganizationId == organizationId, cancellationToken));
    }
    
    private record OrganizationOption(long Id, string Name);

    private record LinkDestination
    {
        public required long OrganizationId { get; init; }
        public required long StatusId { get; init; }
        public required string OrganizationName { get; init; }
        public required string SpaceName { get; init; }
        public required string EpicName { get; init; }
        public required string? StatusName { get; init; }

        public override string ToString()
        {
            var parts = new List<string> { OrganizationName, SpaceName, EpicName };

            if (StatusName is not null)
                parts.Add(StatusName);

            return string.Join(" → ", parts);
        }
    }
}