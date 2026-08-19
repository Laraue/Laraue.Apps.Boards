using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices.Services.Search;

namespace Laraue.Apps.Boards.TelegramServices;

/// <summary>
/// Builds the "📋 KEY · Org" header line shared by every place that shows an issue preview in
/// Telegram: inline search results, /save confirmations, and /info.
/// </summary>
public static class IssuePreviewFormatter
{
    /// <summary>Matches <see cref="ContentFragment"/>'s context window size, so every
    /// preview reads consistently regardless of where it's shown from.</summary>
    public const int FragmentContextChars = 70;

    public static string BuildHeader(IssueKey key, string organizationName)
    {
        return $"📋 *{SearchTextFormatter.EscapeMarkdownV2(key.ToString())}* · {SearchTextFormatter.EscapeMarkdownV2(organizationName)}";
    }
}
