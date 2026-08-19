using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

/// <summary>
/// Sends error/status replies to /save and /info visible only to whoever triggered the command,
/// via Bot API 10.2's ephemeral messages (SendMessageRequest.ReceiverUserId) - so one person's
/// mistyped command doesn't clutter the chat for everyone else.
/// </summary>
public static class EphemeralReplySender
{
    public static Task SendEphemeralNotice(
        this ITelegramBotClient client,
        Message triggeringMessage,
        string text,
        CancellationToken cancellationToken)
    {
        // Ephemeral targeting only makes sense in groups - a private chat already has just the
        // one user talking to the bot.
        var receiverUserId = triggeringMessage.Chat.Type is ChatType.Group or ChatType.Supergroup
            ? triggeringMessage.From!.Id
            : (long?)null;

        return client.SendMessage(
            triggeringMessage.Chat.Id,
            text,
            receiverUserId: receiverUserId,
            cancellationToken: cancellationToken);
    }
}
