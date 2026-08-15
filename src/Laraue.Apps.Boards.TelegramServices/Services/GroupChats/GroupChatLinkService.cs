using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
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
        Guid userId,
        Message message,
        CancellationToken cancellationToken);
}

public class GroupChatLinkService(
    IGroupChatAdminService chatAdminService,
    ITelegramBotClient client,
    DatabaseContext context)
    : IGroupChatLinkService
{
    public async Task HandleLinkCommand(
        Guid userId,
        Message message,
        CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var userTelegramId = message.From!.Id;
        
        if (!await chatAdminService.IsAdmin(chatId, userTelegramId, cancellationToken))
        {
            await client.SendMessage(
                chatId,
                Phrases.LinkRequireAdmin,
                cancellationToken: cancellationToken);
            
            return;
        }

        var linkedChat = await context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == chatId)
            .Select(x => new LinkedChatDto
            {
                EpicName = x.Status!.Epic!.Name,
                StatusName = x.Status.Epic.IsDefault ? null : x.Status.Name,
                SpaceName = x.Status.Epic.Space!.Name,
                OrganizationName = x.Status.Epic.Space!.Organization!.Name,
            })
            .FirstOrDefaultAsyncEF(cancellationToken);

        if (linkedChat is not null)
        {
            await SendAlreadyLinkedMenu(chatId, linkedChat, cancellationToken);
            return;
        }

        await SendOrganizationPicker(chatId, userId, cancellationToken);
    }
    
    private async Task SendOrganizationPicker(
        ChatId chatId,
        Guid userId,
        CancellationToken cancellationToken,
        int? editMessageId = null)
    {
        var organizations = await GetLinkableOrganizations(userId, cancellationToken);

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
            markup = new InlineKeyboardMarkup(organizations
                .Select(org => new[]
                {
                    new CallbackRoutePath(TelegramRoutes.LinkOrganization)
                        .WithQueryParameter("id", org.Id)
                        .ToInlineKeyboardButton($"🏢 {org.Name}")
                }));
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
        CancellationToken cancellationToken)
    {
        var markup = new InlineKeyboardMarkup(
        [
            [new CallbackRoutePath(TelegramRoutes.ChangeLink).ToInlineKeyboardButton(Phrases.LinkChangeLink)],
            [new CallbackRoutePath(TelegramRoutes.Unlink).ToInlineKeyboardButton(Phrases.LinkUnlink)]
        ]);

        await client.SendMessage(
            chatId,
            string.Format(Phrases.LinkAlreadyLinked, linkedChat),
            replyMarkup: markup,
            cancellationToken: cancellationToken);
    }
    
    private Task<List<OrganizationOption>> GetLinkableOrganizations(Guid userId, CancellationToken cancellationToken)
    {
        return context.Organizations
            .Where(o => o.Users!.Any(u => u.UserId == userId && u.AdminAccessLevel.HasFlag(AdminAccessLevel.LinkChats)))
            .OrderBy(o => o.Name)
            .Select(o => new OrganizationOption(o.Id, o.Name))
            .ToListAsyncEF(cancellationToken);
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