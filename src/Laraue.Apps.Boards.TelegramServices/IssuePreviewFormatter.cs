using System.Globalization;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices.Resources;
using Laraue.Apps.Boards.TelegramServices.Services.Search;

namespace Laraue.Apps.Boards.TelegramServices;

/// <summary>
/// Builds the "📋 KEY · Org" header line shared by every place that shows an issue preview in
/// Telegram: inline search results, /save confirmations, and /info.
/// </summary>
public static class IssuePreviewFormatter
{
    public static string BuildHeader(IssueKey key, string organizationName)
    {
        return $"📋 *{SearchTextFormatter.EscapeMarkdownV2(key.ToString())}* · {SearchTextFormatter.EscapeMarkdownV2(organizationName)}";
    }

    /// <summary>
    /// "💬 CHAT · USER · DATE UTC", for issues that came from a Telegram message. Null when any piece
    /// is missing - e.g. the issue was created via the web app, not Telegram.
    /// The time is labeled UTC: a bot doesn't know the reader's time zone, and the card text is
    /// never updated after sending, so a relative "2 days ago" would go stale.
    /// </summary>
    public static string? BuildSourceFooter(string? chatTitle, string? senderName, DateTime? sentAt)
    {
        if (chatTitle is null || senderName is null || sentAt is null)
            return null;

        var text = string.Format(
            CultureInfo.InvariantCulture,
            Phrases.IssuePreviewSourceFooter,
            chatTitle,
            senderName,
            sentAt.Value);
        return $"_{SearchTextFormatter.EscapeMarkdownV2(text)}_";
    }
}
