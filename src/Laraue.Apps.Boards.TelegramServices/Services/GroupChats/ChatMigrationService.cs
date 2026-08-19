using Laraue.Apps.Boards.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.TelegramServices.Services.GroupChats;

public interface IChatMigrationService
{
    /// <summary>
    /// Keeps an existing link pointed at the right chat when Telegram upgrades a basic group to
    /// a supergroup, swapping in a brand new chat id. Without this, an already-linked chat's
    /// link would silently point at a now-dead id from here on.
    /// </summary>
    Task HandleChatMigrated(long oldChatId, long newChatId, CancellationToken cancellationToken);
}

public class ChatMigrationService(DatabaseContext context) : IChatMigrationService
{
    public Task HandleChatMigrated(long oldChatId, long newChatId, CancellationToken cancellationToken)
    {
        return context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == oldChatId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ExternalChatId, newChatId),
                cancellationToken);
    }
}
