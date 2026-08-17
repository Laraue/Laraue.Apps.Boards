using Laraue.Apps.Boards.TelegramServices.Resources;
using Laraue.Apps.Boards.TelegramServices.Services.Messages;
using Laraue.Telegram.NET.Abstractions;
using Telegram.Bot;
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
    ITelegramBotClient client)
    : IGroupChatService
{
    public Task HandleGroupMessage(Message message, CancellationToken cancellationToken)
    {
        // This message was produced by the user picking an inline query result.
        if (message.ViaBot is not null)
            return Task.CompletedTask;

        var request = SaveMessageTelegramRequestFactory.Create(message, requestContext.UserId, message.Chat.Id);

        if (request is null)
        {
            return client.SendMessage(
                message.Chat.Id,
                string.Format(Phrases.MessageTypeIsNotAvailable, message.Type),
                cancellationToken: cancellationToken);
        }

        // Most groups the bot is added to are never linked - stay silent there instead of
        // nagging every message (unlike private chats, which are always linked from
        // registration, so the same notice there signals something actually broke).
        return telegramMessageService.HandleSaveMessage(
            request,
            cancellationToken,
            notifyWhenNotLinked: false);
    }
}
