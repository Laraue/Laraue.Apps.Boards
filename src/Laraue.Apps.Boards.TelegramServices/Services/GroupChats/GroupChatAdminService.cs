using Telegram.Bot;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.TelegramServices.Services.GroupChats;

public interface IGroupChatAdminService
{
    Task<bool> IsAdmin(
        ChatId chatId,
        long telegramUserId,
        CancellationToken cancellationToken);
}

public class GroupChatAdminService(ITelegramBotClient client) : IGroupChatAdminService
{
    public async Task<bool> IsAdmin(
        ChatId chatId,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var member = await client.GetChatMember(chatId, telegramUserId, cancellationToken);

        return member.IsAdmin;
    }
}