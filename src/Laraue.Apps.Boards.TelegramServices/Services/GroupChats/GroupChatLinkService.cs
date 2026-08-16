using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices.Resources;
using Laraue.Telegram.NET.Core.Routing;
using LinqToDB.EntityFrameworkCore;
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
    
    Task HandleOrganizationSelected(
        CallbackQuery query,
        Guid userId,
        long organizationId,
        CancellationToken cancellationToken);
    
    Task HandleBackToOrganizations(
        CallbackQuery query,
        Guid userId,
        CancellationToken cancellationToken);
}

public class GroupChatLinkService(
    IGroupChatAdminService chatAdminService,
    ITelegramBotClient client,
    DatabaseContext context,
    IOrganizationAccessService organizationAccessService,
    IAccessService accessService)
    : IGroupChatLinkService
{
    public async Task HandleLinkCommand(
        Message message,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var userTelegramId = message.From!.Id;
        
        if (!await EnsureIsGroupAdmin(chatId, userTelegramId, cancellationToken))
            return;

        var linkedChat = await GetLinkedChat(chatId, cancellationToken);
        if (linkedChat is not null)
        {
            await SendAlreadyLinkedMenu(chatId, linkedChat, null, cancellationToken);
            return;
        }

        await SendOrganizationPicker(chatId, userId, null, cancellationToken);
    }

    public async Task HandleOrganizationSelected(
        CallbackQuery query,
        Guid userId,
        long organizationId,
        CancellationToken cancellationToken)
    {
        var chatId = query.Message!.Chat.Id;

        if (!await IsAllowedToLink(chatId, query, userId, organizationId, cancellationToken))
            return;

        var organization = await context.Organizations
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
                    .ToInlineKeyboardButton($"📋 {space.Name}")
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
        var userTelegramId = query.From.Id;
        var editedMessageId = query.Message.MessageId;
        
        if (!await EnsureIsGroupAdmin(chatId, userTelegramId, cancellationToken))
            return;

        var linkedChat = await GetLinkedChat(chatId, cancellationToken);
        if (linkedChat is not null)
        {
            await SendAlreadyLinkedMenu(chatId, linkedChat, editedMessageId, cancellationToken);
            return;
        }

        await SendOrganizationPicker(chatId, userId, editedMessageId, cancellationToken);
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

    private async Task<bool> EnsureIsGroupAdmin(ChatId chatId, long telegramUserId, CancellationToken cancellationToken)
    {
        if (await chatAdminService.IsAdmin(chatId, telegramUserId, cancellationToken))
            return true;
        
        await client.SendMessage(
            chatId,
            Phrases.LinkRequireAdmin,
            cancellationToken: cancellationToken);
            
        return false;
    }

    private async Task<bool> IsAllowedToLink(
        ChatId chatId,
        CallbackQuery callbackQuery,
        Guid userId,
        long orgId,
        CancellationToken cancellationToken)
    {
        if (!await chatAdminService.IsAdmin(chatId, callbackQuery.From.Id, cancellationToken))
        {
            await client.AnswerCallbackQuery(
                callbackQuery.Id,
                Phrases.LinkRequireAdmin,
                cancellationToken: cancellationToken);
            
            return false;
        }

        if (!await CanLinkToOrganization(userId, orgId, cancellationToken))
        {
            await client.AnswerCallbackQuery(
                callbackQuery.Id,
                Phrases.LinkRequireAdmin,
                cancellationToken: cancellationToken);
            
            return false;
        }

        return true;
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
        var markup = new InlineKeyboardMarkup(
        [
            [new CallbackRoutePath(TelegramRoutes.ChangeLink).ToInlineKeyboardButton(Phrases.LinkChangeLink)],
            [new CallbackRoutePath(TelegramRoutes.Unlink).ToInlineKeyboardButton(Phrases.LinkUnlink)]
        ]);

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