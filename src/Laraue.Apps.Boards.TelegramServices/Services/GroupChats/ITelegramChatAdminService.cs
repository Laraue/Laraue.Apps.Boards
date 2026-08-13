using Telegram.Bot;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.TelegramServices.Services.GroupChats;

public interface ITelegramChatAdminService
{
    Task<bool> IsAdmin(ChatId chatId, long telegramUserId, CancellationToken cancellationToken);
}

public class TelegramChatAdminService(ITelegramBotClient client) : ITelegramChatAdminService
{
    public async Task<bool> IsAdmin(ChatId chatId, long telegramUserId, CancellationToken cancellationToken)
    {
        var member = await client.GetChatMember(chatId, telegramUserId, cancellationToken);

        return member.IsAdmin;
    }
}
