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

        var linkedChat = await GetLinkedChat(chatId, cancellationToken);
        if (linkedChat is not null)
        {
            await SendAlreadyLinkedMenu(chatId, linkedChat, null, cancellationToken);
            return;
        }

        await SendOrganizationPicker(chatId, userId, null, cancellationToken);
    }

    public async Task HandleUnlinkCommand(Message message, Guid userId, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        if (!await EnsureUserIsGroupAdmin(message, cancellationToken))
            return;
        
        var linkedChat = await GetLinkedChat(chatId, cancellationToken);
        if (linkedChat is null)
        {
            // TODO - say something?
            return;
        }

        await context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == chatId)
            .ExecuteDeleteAsync(cancellationToken);
        
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

        var organization = await context.Organizations
            .Where(x => x.Id == organizationId)
            .Select(x => new { x.Name })
            .FirstAsyncEF(cancellationToken);
        
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

        var linkedChat = await GetLinkedChat(chatId, cancellationToken);
        if (linkedChat is not null)
        {
            await SendAlreadyLinkedMenu(chatId, linkedChat, editedMessageId, cancellationToken);
            return;
        }

        await SendOrganizationPicker(chatId, userId, editedMessageId, cancellationToken);
    }

    public async Task HandleSpaceSelected(CallbackQuery query, Guid userId, long spaceId, CancellationToken cancellationToken)
    {
        var chatId = query.Message!.Chat.Id;

        var space = await context.Spaces
            .Where(x => x.Id == spaceId)
            .Select(x => new
            {
                x.OrganizationId,
                x.Name,
                OrganizationName = x.Organization!.Name
            })
            .SingleAsyncEF(cancellationToken);
        
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
                var epicIcon = e.IsDefault ? "✅" : "📋";
                
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
        var epic = await context.Epics
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
            .SingleAsyncEF(cancellationToken);
        
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
            var status = statuses.First();
            
            await LinkToStatus(
                chatId,
                query.Message.Chat.Title,
                status.Id,
                userId, 
                cancellationToken);

            await SendLinkConfirmed(
                chatId,
                query.Message.MessageId,
                new LinkedChatDto
                {
                    OrganizationName = epic.OrganizationName,
                    SpaceName = epic.SpaceName,
                    EpicName = epic.Name,
                    StatusName = null,
                },
                cancellationToken);

            return;
        }
        
        var buttons = statuses
            .Select(status => new[]
            {
                new CallbackRoutePath(TelegramRoutes.LinkStatus)
                    .WithPathParameter("id", status.Id.ToString())
                    .ToInlineKeyboardButton($"✅ {status.Name}")
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
        var status = await context.Statuses
            .Where(x => x.Id == statusId)
            .Select(x => new
            {
                OrganizationName = x.Epic!.Space!.Organization!.Name,
                x.Epic.Space!.OrganizationId,
                SpaceName = x.Epic.Space.Name,
                SpaceId = x.Epic.Space.Id,
                EpicName = x.Epic.Name,
                x.Name,
                x.Id,
            })
            .SingleAsyncEF(cancellationToken); // TODO - in all such cases response with status not exists or was deleted. Avoid unhandled exceptions in TG handlers
        
        if (!await IsAllowedToLink(query, userId, status.OrganizationId, cancellationToken))
            return;
        
        await LinkToStatus(
            chatId,
            query.Message.Chat.Title,
            status.Id,
            userId, 
            cancellationToken);

        await SendLinkConfirmed(
            chatId,
            query.Message.MessageId,
            new LinkedChatDto
            {
                OrganizationName = status.OrganizationName,
                SpaceName = status.SpaceName,
                EpicName = status.EpicName,
                StatusName = status.Name,
            },
            cancellationToken);
    }

    public async Task HandleUnlink(CallbackQuery query, Guid userId, CancellationToken cancellationToken)
    {
        var chatId = query.Message!.Chat.Id;
        var linkedChat = await context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == chatId)
            .Select(x => new { x.Status!.Epic!.Space!.OrganizationId })
            .FirstOrDefaultAsyncEF(cancellationToken);
        
        if (linkedChat is null)
            return; // TODO - answer here
        
        if (!await IsAllowedToLink(query, userId, linkedChat.OrganizationId, cancellationToken))
            return;  // TODO - answer here
        
        await context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == chatId)
            .ExecuteDeleteAsync(cancellationToken);
        
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
        chat.LinkedAt = dateTimeProvider.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
    }
    
    private async Task SendLinkConfirmed(
        ChatId chatId,
        int messageId,
        LinkedChatDto destination,
        CancellationToken cancellationToken)
    {
        var bot = await client.GetMe(cancellationToken);

        await client.EditMessageText(
            chatId,
            messageId,
            string.Format(Phrases.LinkConfirmed, destination, bot.Username),
            cancellationToken: cancellationToken);
    }

    private Task<LinkedChatDto?> GetLinkedChat(long chatId, CancellationToken cancellationToken)
    {
        return context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == chatId)
            .Select(x => new LinkedChatDto
            {
                EpicName = x.Status!.Epic!.Name,
                StatusName = x.Status.Epic.IsDefault ? null : x.Status.Name,
                SpaceName = x.Status.Epic.Space!.Name,
                OrganizationName = x.Status.Epic.Space!.Organization!.Name,
            })
            .SingleOrDefaultAsyncEF(cancellationToken);
    }

    private async Task<bool> EnsureUserIsGroupAdmin(
        Message message,
        CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var userTelegramId = message.From!.Id;
        
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

        if (editMessageId is not null)
        {
            await client.EditMessageText(
                chatId,
                editMessageId.Value,
                text,
                replyMarkup: markup,
                cancellationToken: cancellationToken);

            return;
        }

        await client.SendMessage(
            chatId,
            text,
            replyMarkup: markup,
            cancellationToken: cancellationToken);
    }
    
    private async Task SendAlreadyLinkedMenu(
        ChatId chatId,
        LinkedChatDto linkedChat,
        int? editMessageId,
        CancellationToken cancellationToken)
    {
        var buttons = new []
        {
            new [] { new CallbackRoutePath(TelegramRoutes.Unlink).ToInlineKeyboardButton(Phrases.LinkUnlink) }
        }.AddCancelButton();
        
        var markup = new InlineKeyboardMarkup(buttons);

        var text = string.Format(Phrases.LinkAlreadyLinked, linkedChat);
        
        if (editMessageId is not null)
        {
            await client.EditMessageText(
                chatId,
                editMessageId.Value,
                text,
                replyMarkup: markup,
                cancellationToken: cancellationToken);

            return;
        }
        
        await client.SendMessage(
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
                .Select(o => new OrganizationOption(o.Id, o.Organization!.Name))
                .ToListAsyncEF(cancellationToken));
    }

    private Task<bool> CanLinkToOrganization(Guid userId, long organizationId, CancellationToken cancellationToken)
    {
        return organizationAccessService.GetOrganizations(
            userId,
            query => query
                .AnyAsyncEF(x => x.Id == organizationId, cancellationToken));
    }
    
    private record OrganizationOption(long Id, string Name);

    private record LinkedChatDto
    {
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