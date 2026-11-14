using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.TelegramServices.Resources;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public interface IReplyModeService
{
    /// <summary>
    /// Handles the "react instead of replying" button on a link reply: switches the chat to
    /// <see cref="ReplyMode.Reaction"/>, keeps the link but drops the button, and reacts 👍 to the saved message.
    /// </summary>
    Task HandleSwitchToReactions(
        CallbackQuery callbackQuery,
        Guid userId,
        CancellationToken cancellationToken);
}

public class ReplyModeService(DatabaseContext context, ITelegramBotClient client) : IReplyModeService
{
    public async Task HandleSwitchToReactions(
        CallbackQuery callbackQuery,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var message = callbackQuery.Message!;

        // The button is only offered in personal chats, which only their owner can write to.
        var updated = await context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == message.Chat.Id && x.UnlinkedAt == null && x.OwnerId == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.ReplyMode, ReplyMode.Reaction), cancellationToken);

        if (updated == 0)
        {
            await client.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            return;
        }

        // The first row is the card's Open button - the link stays, only the switch goes.
        var linkRow = message.ReplyMarkup?.InlineKeyboard.FirstOrDefault();
        await client.EditMessageReplyMarkup(
            message.Chat.Id,
            message.MessageId,
            linkRow is null ? null : new InlineKeyboardMarkup(linkRow),
            cancellationToken: cancellationToken);

        // The link reply answers the saved message; it's gone only if the user deleted that message.
        if (message.ReplyToMessage is { } savedMessage)
        {
            await client.SetMessageReaction(
                message.Chat.Id,
                savedMessage.MessageId,
                [new ReactionTypeEmoji { Emoji = "👍" }],
                cancellationToken: cancellationToken);
        }

        await client.AnswerCallbackQuery(
            callbackQuery.Id,
            Phrases.SwitchedToReactions,
            cancellationToken: cancellationToken);
    }
}
