using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Telegram.NET.Authentication.Services;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.TelegramServices;

public class TelegramUserQueryService(DatabaseContext context, ICoreUserService userService)
    : ITelegramUserQueryService<Guid>
{
    public Task<TelegramUserId<Guid>?> FindUserIdAsync(long telegramId, CancellationToken cancellationToken = default)
    {
        return context.Users
            .Where(u => u.TelegramId == telegramId)
            .Select(u => new TelegramUserId<Guid>(u.Id))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Guid> CreateAsync(TelegramData telegramData, CancellationToken cancellationToken = default)
    {
        return userService.CreateIfTelegramIdNotExists(
            new TelegramUserProfile(
                telegramData.Id,
                telegramData.Username,
                telegramData.FirstName,
                telegramData.LastName,
                telegramData.LanguageCode),
            cancellationToken);
    }
}
