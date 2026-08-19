using Laraue.Apps.Boards.TelegramServices.Services.GroupChats;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

/// <summary>
/// Test double for <see cref="IGroupChatAdminService"/> — avoids having to fake Telegram's
/// GetChatMember response shape just to control whether the acting user is treated as a
/// group admin. Registered permanently in <see cref="TelegramIntegrationTest.GetTelegramTestHost"/>;
/// tests pick admin/non-admin behavior by sending the update from
/// <see cref="TelegramIntegrationTest.AdminUser"/> or <see cref="TelegramIntegrationTest.MemberUser"/>.
/// </summary>
public class FakeGroupChatAdminService : IGroupChatAdminService
{
    public const long AdminTelegramUserId = 100_001;
    public const long MemberTelegramUserId = 100_002;

    public Task<bool> IsAdmin(ChatId chatId, long telegramUserId, CancellationToken cancellationToken)
    {
        return Task.FromResult(telegramUserId == AdminTelegramUserId);
    }
}
