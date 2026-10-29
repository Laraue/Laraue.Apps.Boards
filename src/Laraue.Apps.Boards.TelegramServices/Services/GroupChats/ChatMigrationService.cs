using Laraue.Apps.Boards.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.TelegramServices.Services.GroupChats;

public interface IChatMigrationService
{
    /// <summary>
    /// Keeps an existing link pointed at the right chat when Telegram upgrades a basic group to
    /// a supergroup, swapping in a brand new chat id. Without this, an already-linked chat's
    /// link would silently point at a now-dead id from here on.
    /// Messages saved before the upgrade keep the old chat id on purpose: the supergroup numbers its messages anew,
    /// so an old message id there means a different message (or none) - rewriting it would build a wrong t.me link
    /// and could collide with a new message's (chat id, message id). The old id leaves them without a link, as a
    /// basic group's messages are.
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
