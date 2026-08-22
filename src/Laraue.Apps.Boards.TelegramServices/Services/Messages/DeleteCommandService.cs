using Laraue.Apps.Boards.TelegramServices.Resources;
using Laraue.Apps.Boards.TelegramServices.Services.GroupChats;
using Laraue.Telegram.NET.Core.Routing;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public interface IDeleteCommandService
{
    /// <summary>
    /// Handles /delete: reply to a message with a card to ask for confirmation before deleting
    /// it. Never deletes anything on its own - see <see cref="HandleDeleteConfirmed"/>.
    /// </summary>
    Task HandleDeleteCommand(Message message, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Handles the Confirm button tap from <see cref="HandleDeleteCommand"/>'s prompt.
    /// </summary>
    Task HandleDeleteConfirmed(
        CallbackQuery callbackQuery,
        Guid userId,
        long issueId,
        CancellationToken cancellationToken);
}

public class DeleteCommandService(
    ITelegramSaveMessageService saveMessageService,
    ITelegramBotClient client,
    IEphemeralReplySender ephemeralReplySender)
    : IDeleteCommandService
{
    public async Task HandleDeleteCommand(Message message, Guid userId, CancellationToken cancellationToken)
    {
        var repliedMessage = message.ReplyToMessage;

        // Not every genuine reply carries reply_to_message (Telegram omits it for old enough
        // messages), so this also covers that case, not just "user didn't reply at all".
        if (repliedMessage is null)
        {
            await ephemeralReplySender.SendEphemeralNotice(message, Phrases.DeleteNotAReply, cancellationToken);
            return;
        }

        // The bot's own messages are never recorded as TelegramMessage rows, so this would
        // otherwise surface as the more confusing "not on record" outcome below.
        if (repliedMessage.From?.IsBot == true)
        {
            await ephemeralReplySender.SendEphemeralNotice(message, Phrases.SaveMessageFromBot, cancellationToken);
            return;
        }

        var result = await saveMessageService.GetDeleteConfirmation(
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
                var buttons = new[]
                {
                    new[]
                    {
                        new CallbackRoutePath(TelegramRoutes.DeleteConfirm)
                            .WithPathParameter("issueId", result.IssueId!.Value.ToString())
                            .ToInlineKeyboardButton(Phrases.DeleteConfirmButton),
                    },
                }.AddCancelButton();

                await client.SendMessage(
                    message.Chat.Id,
                    string.Format(Phrases.DeleteConfirmPrompt, result.IssuePreviewText),
                    parseMode: ParseMode.MarkdownV2,
                    replyParameters: new ReplyParameters
                    {
                        MessageId = repliedMessage.MessageId,
                        AllowSendingWithoutReply = true,
                    },
                    replyMarkup: new InlineKeyboardMarkup(buttons),
                    cancellationToken: cancellationToken);
                break;

            case InfoByReplyOutcome.NoCardYet:
                await ephemeralReplySender.SendEphemeralNotice(message, Phrases.InfoNoCardYet, cancellationToken);
                break;

            case InfoByReplyOutcome.MessageNotTracked:
                await ephemeralReplySender.SendEphemeralNotice(message, Phrases.InfoMessageNotTracked, cancellationToken);
                break;

            case InfoByReplyOutcome.Forbidden:
                await ephemeralReplySender.SendEphemeralNotice(message, Phrases.DeleteForbidden, cancellationToken);
                break;
        }
    }

    public async Task HandleDeleteConfirmed(
        CallbackQuery callbackQuery,
        Guid userId,
        long issueId,
        CancellationToken cancellationToken)
    {
        var deleted = await saveMessageService.DeleteIssue(issueId, userId, cancellationToken);

        if (!deleted)
        {
            await client.AnswerCallbackQuery(
                callbackQuery.Id,
                Phrases.DeleteForbidden,
                showAlert: true,
                cancellationToken: cancellationToken);
            return;
        }

        var message = callbackQuery.Message!;

        await client.EditMessageText(
            message.Chat.Id,
            message.MessageId,
            Phrases.DeleteCommandDeleted,
            cancellationToken: cancellationToken);
    }
}
