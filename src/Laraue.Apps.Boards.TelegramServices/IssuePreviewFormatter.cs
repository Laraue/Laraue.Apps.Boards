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

    /// <summary>
    /// "💬 CHAT · USER · DATE", for issues that came from a Telegram message. Null when any piece
    /// is missing - e.g. the issue was created via the web app, not Telegram.
    /// </summary>
    public static string? BuildSourceFooter(string? chatTitle, string? senderName, DateTime? sentAt)
    {
        if (chatTitle is null || senderName is null || sentAt is null)
            return null;

        var text = $"💬 {chatTitle} · {senderName} · {sentAt.Value:yyyy-MM-dd HH:mm}";
        return $"_{SearchTextFormatter.EscapeMarkdownV2(text)}_";
    }

    /// <summary>
    /// Fallback preview text for when building the real one (headers/bullets/code fences/search
    /// highlighting, all hand-rolled - see <see cref="TelegramMarkdownFormatter"/>) throws.
    /// Callers should catch, log the exception with the issue's key, and send this instead - so a
    /// bug in formatting one issue's content degrades to "this one preview looks wrong" rather
    /// than failing the whole request (an inline search's entire result batch, or a /save reply).
    /// Still goes through <see cref="BuildHeader"/> and normal escaping, so it's exactly as safe
    /// to send with MarkdownV2 as everything else here - failing to build a preview is not a
    /// license to skip escaping and risk a *second*, harder-to-diagnose failure.
    /// </summary>
    public static string BuildContentGenerationErrorText(IssueKey key, string organizationName)
    {
        return BuildHeader(key, organizationName) + "\n" +
            SearchTextFormatter.EscapeMarkdownV2("⚠️ Something went wrong while generating the content of this message.");
    }
}
