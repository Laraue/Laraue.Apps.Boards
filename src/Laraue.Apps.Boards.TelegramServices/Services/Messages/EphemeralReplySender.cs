using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public interface IEphemeralReplySender
{
    /// <summary>
    /// Sends an error/status reply to /save or /info visible only to whoever triggered the
    /// command, via Bot API 10.2's ephemeral messages (SendMessageRequest.ReceiverUserId) - so
    /// one person's mistyped command doesn't clutter the chat for everyone else.
    /// </summary>
    Task SendEphemeralNotice(Message triggeringMessage, string text, CancellationToken cancellationToken);
}

public class EphemeralReplySender(ITelegramBotClient client, ILogger<EphemeralReplySender> logger)
    : IEphemeralReplySender
{
    public async Task SendEphemeralNotice(Message triggeringMessage, string text, CancellationToken cancellationToken)
    {
        // Ephemeral targeting only makes sense in groups - a private chat already has just the
        // one user talking to the bot.
        var receiverUserId = triggeringMessage.Chat.Type is ChatType.Group or ChatType.Supergroup
            ? triggeringMessage.From!.Id
            : (long?)null;

        try
        {
            await client.SendMessage(
                triggeringMessage.Chat.Id,
                text,
                receiverUserId: receiverUserId,
                cancellationToken: cancellationToken);
        }
        catch (ApiRequestException ex) when (receiverUserId is not null)
        {
            // Telegram can reject an ephemeral send for reasons we can't fully enumerate (the
            // bot not being a group admin is the one we've confirmed, there may be others tied
            // to chat type/settings) - whatever the reason, fall back to a normal, chat-visible
            // reply so the command still gets an answer instead of silently failing.
            logger.LogWarning(
                ex,
                "Ephemeral message to chat {ChatId} failed, falling back to a public reply",
                triggeringMessage.Chat.Id);

            await client.SendMessage(
                triggeringMessage.Chat.Id,
                text,
                cancellationToken: cancellationToken);
        }
    }
}
