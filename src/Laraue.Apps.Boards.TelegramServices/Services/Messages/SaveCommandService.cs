using Laraue.Apps.Boards.TelegramServices.Resources;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public interface ISaveCommandService
{
    /// <summary>
    /// Handles /save: manually turns the replied-to message (or its whole album) into a card.
    /// Only meaningful in BotMentionedMessages mode.
    /// </summary>
    Task HandleSaveCommand(Message message, Guid userId, CancellationToken cancellationToken);
}

public class SaveCommandService(
    ITelegramSaveMessageService saveMessageService,
    ITelegramBotClient client)
    : ISaveCommandService
{
    public async Task HandleSaveCommand(Message message, Guid userId, CancellationToken cancellationToken)
    {
        var repliedMessage = message.ReplyToMessage;

        // Not every genuine reply carries reply_to_message (Telegram omits it for old enough
        // messages), so this also covers that case, not just "user didn't reply at all".
        if (repliedMessage is null)
        {
            await client.SendMessage(message.Chat.Id, Phrases.SaveNotAReply, cancellationToken: cancellationToken);
            return;
        }

        // The bot's own messages (card previews, error replies, etc.) are never recorded as
        // TelegramMessage rows, so this would otherwise surface as the more confusing
        // "not on record" outcome below.
        if (repliedMessage.From?.IsBot == true)
        {
            await client.SendMessage(message.Chat.Id, Phrases.SaveMessageFromBot, cancellationToken: cancellationToken);
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
                    Note = ExtractNote(message.Text),
                },
                cancellationToken);
        }
        catch (ChatNotLinkedException)
        {
            await client.SendMessage(message.Chat.Id, Phrases.LinkNotLinked, cancellationToken: cancellationToken);
            return;
        }
        catch (IssueCreationForbiddenException)
        {
            await client.SendMessage(message.Chat.Id, Phrases.IssueCreationForbidden, cancellationToken: cancellationToken);
            return;
        }

        switch (result.Outcome)
        {
            // /save is a deliberate, visible action everyone in the chat just watched happen -
            // a reply with the link is more useful here than a reaction only the sender might
            // notice. Reactions stay reserved for silent auto-save. The card itself (key, org,
            // content preview) already makes clear something was saved - no extra status line
            // needed, whether this is the first save or a repeat.
            case SaveByReplyOutcome.Saved:
            case SaveByReplyOutcome.AlreadySaved:
                await client.SendIssuePreviewReply(
                    message.Chat.Id,
                    repliedMessage.MessageId,
                    result.IssuePreviewText!,
                    result.IssueUrl!,
                    cancellationToken);
                break;

            case SaveByReplyOutcome.NotNeededInAutoMode:
                await client.SendMessage(message.Chat.Id, Phrases.SaveNotNeededInAutoMode, cancellationToken: cancellationToken);
                break;

            case SaveByReplyOutcome.MessageNotTracked:
                await client.SendMessage(message.Chat.Id, Phrases.SaveMessageNotTracked, cancellationToken: cancellationToken);
                break;

            case SaveByReplyOutcome.NothingToSave:
                await client.SendMessage(message.Chat.Id, Phrases.SaveNothingToSave, cancellationToken: cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Everything after the first space in "/save some note" -&gt; "some note" (also strips a
    /// bot @mention, e.g. "/save@mybot some note"). Null when the command was sent bare.
    /// </summary>
    private static string? ExtractNote(string? commandText)
    {
        if (string.IsNullOrEmpty(commandText))
            return null;

        var spaceIndex = commandText.IndexOf(' ');
        if (spaceIndex < 0)
            return null;

        // Trim in span-space first so a bare "/save " (no real note) doesn't allocate at all,
        // and a note with surrounding whitespace only allocates once, for the final string.
        var note = commandText.AsSpan(spaceIndex + 1).Trim();
        return note.IsEmpty ? null : note.ToString();
    }
}
