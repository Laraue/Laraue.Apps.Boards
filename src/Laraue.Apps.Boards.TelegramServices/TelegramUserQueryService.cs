using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Telegram.NET.Authentication.Services;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.TelegramServices;

public class TelegramUserQueryService(
    DatabaseContext context,
    ICoreUserService userService,
    RequestContext requestContext)
    : ITelegramUserQueryService<Guid>
{
    public Task<TelegramUserId<Guid>?> FindUserIdAsync(long telegramId, CancellationToken cancellationToken = default)
    {
        return context.Users
            .Where(u => u.TelegramId == telegramId)
            .Select(u => new TelegramUserId<Guid>(u.Id))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid> CreateAsync(TelegramData telegramData, CancellationToken cancellationToken = default)
    {
        var profile = new TelegramUserProfile(
            telegramData.Id,
            telegramData.Username,
            telegramData.FirstName,
            telegramData.LastName,
            telegramData.LanguageCode,
            // A user is created by their first update, which for a deep link (t.me/<bot>?start=producthunt) is
            // "/start producthunt": where they came from (BRD-295).
            SignUpOrigin.FromStartCommand(requestContext.Update.Message?.Text));
        var identity = await userService.ResolveTelegramIdentity(profile, cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var userId = await userService.CreateIfTelegramIdNotExists(profile, identity, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return userId;
    }
}
