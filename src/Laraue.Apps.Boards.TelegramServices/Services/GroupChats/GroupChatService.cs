using Laraue.Apps.Boards.TelegramServices.Services.Messages;
using Laraue.Telegram.NET.Abstractions;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.TelegramServices.Services.GroupChats;

/// <summary>
/// Handles messages in group/supergroup chats: detects commands and turns the
/// replied-to message into a cards, per the linked chat's destination.
/// </summary>
public interface IGroupChatService
{
    /// <summary>
    /// Entry point for every Message/EditedMessage update from a group/supergroup chat that
    /// wasn't otherwise routed (e.g. not a /link or /unlink command).
    /// </summary>
    Task HandleGroupMessage(Message message, CancellationToken cancellationToken);
}

public class GroupChatService(
    RequestContext requestContext,
    ITelegramMessageService telegramMessageService,
    IChatMigrationService chatMigrationService)
    : IGroupChatService
{
    public async Task HandleGroupMessage(Message message, CancellationToken cancellationToken)
    {
        // Telegram posts this service message in the old chat when it upgrades a basic group to
        // a supergroup, announcing the new chat id.
        if (message.MigrateToChatId is { } newChatId)
        {
            await chatMigrationService.HandleChatMigrated(message.Chat.Id, newChatId, cancellationToken);
            return;
        }

        // This message was produced by the user picking an inline query result.
        if (message.ViaBot is not null)
            return;

        var request = SaveMessageTelegramRequestFactory.Create(message, requestContext.UserId, message.Chat.Id);

        // Unsupported message types (stickers, polls, etc.) are just silently skipped.
        if (request is null)
            return;

        // Most groups the bot is added to are never linked - stay silent there instead of
        // nagging every message.
        await telegramMessageService.HandleSaveMessage(
            request,
            cancellationToken,
            notifyOnFailure: false);
    }
}
