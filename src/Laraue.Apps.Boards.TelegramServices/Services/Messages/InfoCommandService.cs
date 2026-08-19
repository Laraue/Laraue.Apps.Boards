using Laraue.Apps.Boards.TelegramServices.Resources;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public interface IInfoCommandService
{
    /// <summary>
    /// Handles /info: read-only lookup of the card already linked to the replied-to message (or
    /// its whole album), if any. Works regardless of save mode - unlike /save, never creates or
    /// changes anything.
    /// </summary>
    Task HandleInfoCommand(Message message, Guid userId, CancellationToken cancellationToken);
}

public class InfoCommandService(
    ITelegramSaveMessageService saveMessageService,
    ITelegramBotClient client)
    : IInfoCommandService
{
    public async Task HandleInfoCommand(Message message, Guid userId, CancellationToken cancellationToken)
    {
        var repliedMessage = message.ReplyToMessage;

        // Not every genuine reply carries reply_to_message (Telegram omits it for old enough
        // messages), so this also covers that case, not just "user didn't reply at all".
        if (repliedMessage is null)
        {
            await client.SendMessage(message.Chat.Id, Phrases.InfoNotAReply, cancellationToken: cancellationToken);
            return;
        }

        // The bot's own messages are never recorded as TelegramMessage rows, so this would
        // otherwise surface as the more confusing "not on record" outcome below.
        if (repliedMessage.From?.IsBot == true)
        {
            await client.SendMessage(message.Chat.Id, Phrases.InfoMessageFromBot, cancellationToken: cancellationToken);
            return;
        }

        var result = await saveMessageService.GetInfoByReply(
            new InfoByReplyRequest
            {
                ExternalChatId = message.Chat.Id,
                RepliedExternalMessageId = repliedMessage.MessageId,
                UserId = userId,
            },
            cancellationToken);

        switch (result.Outcome)
        {
            case InfoByReplyOutcome.Found:
                await client.SendIssuePreviewReply(
                    message.Chat.Id,
                    repliedMessage.MessageId,
                    result.IssuePreviewText!,
                    result.IssueUrl!,
                    cancellationToken);
                break;

            case InfoByReplyOutcome.NoCardYet:
                await client.SendMessage(message.Chat.Id, Phrases.InfoNoCardYet, cancellationToken: cancellationToken);
                break;

            case InfoByReplyOutcome.MessageNotTracked:
                await client.SendMessage(message.Chat.Id, Phrases.InfoMessageNotTracked, cancellationToken: cancellationToken);
                break;

            case InfoByReplyOutcome.Forbidden:
                await client.SendMessage(message.Chat.Id, Phrases.InfoForbidden, cancellationToken: cancellationToken);
                break;
        }
    }
}
