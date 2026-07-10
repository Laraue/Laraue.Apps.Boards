using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Telegram.NET.Authentication.Services;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.TelegramServices;

public class TelegramUserQueryService(DatabaseContext context, IDateTimeProvider dateTimeProvider, ICoreUserService userService)
    : ITelegramUserQueryService<User, Guid>
{
    public Task<User?> FindAsync(long telegramId, CancellationToken cancellationToken = default)
    {
        return context.Users
            .Where(u => u.TelegramId == telegramId)
            .FirstOrDefaultAsyncEF(cancellationToken);
    }

    public Task<Guid> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        return userService.CreateIfTelegramIdNotExists(user, cancellationToken);
    }
}