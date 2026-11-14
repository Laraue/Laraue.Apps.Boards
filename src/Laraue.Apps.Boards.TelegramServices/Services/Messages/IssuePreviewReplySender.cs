using Laraue.Apps.Boards.TelegramServices.Resources;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

/// <summary>
/// Sends the same key/org/content-preview "card" shown for an inline search result, so /save and
/// /info replies look the same.
/// </summary>
public static class IssuePreviewReplySender
{
    public static Task SendIssuePreviewReply(
        this ITelegramBotClient client,
        long chatId,
        int repliedMessageId,
        string issuePreviewText,
        string issueUrl,
        CancellationToken cancellationToken,
        InlineKeyboardButton? extraButton = null)
    {
        InlineKeyboardButton[][] buttons = extraButton is null
            ? [[OpenIssueButton(issueUrl)]]
            : [[OpenIssueButton(issueUrl)], [extraButton]];

        return client.SendMessage(
            chatId,
            issuePreviewText,
            parseMode: ParseMode.MarkdownV2,
            replyParameters: new ReplyParameters
            {
                MessageId = repliedMessageId,
                AllowSendingWithoutReply = true,
            },
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }

    public static InlineKeyboardButton OpenIssueButton(string issueUrl)
    {
        return InlineKeyboardButton.WithUrl(Phrases.OpenIssueButton, issueUrl);
    }
}
