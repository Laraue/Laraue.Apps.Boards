using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Ai;
using Laraue.Apps.Boards.Services.Billing;
using Laraue.Apps.Boards.TelegramServices.Resources;
using Laraue.Apps.Boards.TelegramServices.Services.Search;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public interface ISaveCommandService
{
    /// <summary>
    /// Handles /save: manually turns the replied-to message (or its whole album) into a card.
    /// "/save Some title" sets the title directly; a bare "/save" takes it from the first line of the message
    /// text (no AI), and asks for an explicit title when there is no text. Only meaningful in
    /// BotMentionedMessages mode.
    /// </summary>
    Task HandleSaveCommand(Message message, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Handles /aisave: same as /save, but the AI always rewrites the text (see
    /// <see cref="IAiContentSummarizer"/>) into the description. "/aisave Some title" keeps the given title as it
    /// is; a bare "/aisave" has the AI write the title too.
    /// </summary>
    Task HandleAiSaveCommand(Message message, Guid userId, CancellationToken cancellationToken);
}

public class SaveCommandService(
    ITelegramSaveMessageService saveMessageService,
    ITelegramBotClient client,
    IEphemeralReplySender ephemeralReplySender,
    ISaveDestinationPicker destinationPicker,
    ILogger<SaveCommandService> logger)
    : ISaveCommandService
{
    public Task HandleSaveCommand(Message message, Guid userId, CancellationToken cancellationToken)
    {
        return HandleSaveCommand(message, userId, summarize: false, cancellationToken);
    }

    public Task HandleAiSaveCommand(Message message, Guid userId, CancellationToken cancellationToken)
    {
        return HandleSaveCommand(message, userId, summarize: true, cancellationToken);
    }

    private async Task HandleSaveCommand(Message message, Guid userId, bool summarize, CancellationToken cancellationToken)
    {
        var repliedMessage = message.ReplyToMessage;

        // Not every genuine reply carries reply_to_message (Telegram omits it for old enough
        // messages), so this also covers that case, not just "user didn't reply at all".
        if (repliedMessage is null)
        {
            await ephemeralReplySender.SendEphemeralNotice(message, Phrases.SaveNotAReply, cancellationToken);
            return;
        }

        // The bot's own messages (card previews, error replies, etc.) are never recorded as
        // TelegramMessage rows, so this would otherwise surface as the more confusing
        // "not on record" outcome below.
        if (repliedMessage.From?.IsBot == true)
        {
            await ephemeralReplySender.SendEphemeralNotice(message, Phrases.SaveMessageFromBot, cancellationToken);
            return;
        }

        // Commands (e.g. a bare "/save" someone replies to) are never recorded as content, so
        // this would otherwise surface as the more confusing "not on record" outcome below.
        if (repliedMessage.IsBotCommand())
        {
            await ephemeralReplySender.SendEphemeralNotice(message, Phrases.ReplyTargetIsCommand, cancellationToken);
            return;
        }

        SaveByReplyResult result;
        try
        {
            result = await saveMessageService.SaveByReply(
                new SaveByReplyRequest
                {
                    ExternalChatId = message.Chat.Id,
                    RepliedExternalMessageId = repliedMessage.MessageId,
                    UserId = userId,
                    Title = SaveCommandText.ExtractTitle(message.Text),
                    Summarize = summarize,
                },
                cancellationToken);
        }
        catch (Exception ex) when (SaveRefusal.GetMessage(ex) is { } refusal)
        {
            if (ex is AiContentSummarizationException)
                logger.LogWarning(ex, "AI generation failed for chat {ExternalChatId}", message.Chat.Id);

            await ephemeralReplySender.SendEphemeralNotice(message, refusal, cancellationToken);
            return;
        }

        // Documents that weren't saved are always reported, under whatever the reply is.
        var skippedFilesNotice = result.SkippedFiles.Count > 0
            ? SkippedFile.FormatNotice(result.SkippedFiles)
            : null;

        switch (result.Outcome)
        {
            // The chat's reply mode decides, as for auto-save (BRD-361): a reaction on the saved message - 👍 for a
            // new card, ❤ for an update. Documents that weren't saved still need words, so they come as a reply.
            case SaveByReplyOutcome.Saved or SaveByReplyOutcome.AlreadySaved
                when result.ReplyMode == ReplyMode.Reaction:
                await client.SetMessageReaction(
                    message.Chat.Id,
                    repliedMessage.MessageId,
                    [new ReactionTypeEmoji { Emoji = result.Outcome == SaveByReplyOutcome.Saved ? "👍" : "❤" }],
                    cancellationToken: cancellationToken);

                if (skippedFilesNotice is not null)
                {
                    await client.SendMessage(
                        message.Chat.Id,
                        skippedFilesNotice,
                        replyParameters: new ReplyParameters
                        {
                            MessageId = repliedMessage.MessageId,
                            AllowSendingWithoutReply = true,
                        },
                        cancellationToken: cancellationToken);
                }

                break;

            // Link mode: the card itself (key, org, content preview) makes clear something was saved - no extra
            // status line needed, whether this is the first save or a repeat.
            case SaveByReplyOutcome.Saved:
            case SaveByReplyOutcome.AlreadySaved:
                await client.SendIssuePreviewReply(
                    message.Chat.Id,
                    repliedMessage.MessageId,
                    skippedFilesNotice is null
                        ? result.IssuePreviewText!
                        : $"{result.IssuePreviewText}\n\n{SearchTextFormatter.EscapeMarkdownV2(skippedFilesNotice)}",
                    result.IssueUrl!,
                    cancellationToken);
                break;

            // The chat chooses part of its destination each time (BRD-365): the picker answers this /save command,
            // so a tap can read its title and /aisave, and who sent it.
            case SaveByReplyOutcome.DestinationRequired:
                await destinationPicker.SendPicker(
                    message.Chat.Id,
                    message.MessageId,
                    result.TelegramMessageId!.Value,
                    userId,
                    cancellationToken);
                break;

            case SaveByReplyOutcome.NotNeededInAutoMode:
                await ephemeralReplySender.SendEphemeralNotice(message, Phrases.SaveNotNeededInAutoMode, cancellationToken);
                break;

            case SaveByReplyOutcome.MessageNotTracked:
                await ephemeralReplySender.SendEphemeralNotice(message, Phrases.SaveMessageNotTracked, cancellationToken);
                break;

            case SaveByReplyOutcome.NothingToSave:
                await ephemeralReplySender.SendEphemeralNotice(
                    message,
                    WithSkippedFilesNotice(Phrases.SaveNothingToSave, skippedFilesNotice),
                    cancellationToken);
                break;

            case SaveByReplyOutcome.TitleRequired:
                await ephemeralReplySender.SendEphemeralNotice(
                    message,
                    WithSkippedFilesNotice(Phrases.SaveTitleRequired, skippedFilesNotice),
                    cancellationToken);
                break;
        }
    }

    private static string WithSkippedFilesNotice(string text, string? skippedFilesNotice)
    {
        return skippedFilesNotice is null ? text : $"{text}\n\n{skippedFilesNotice}";
    }
}
