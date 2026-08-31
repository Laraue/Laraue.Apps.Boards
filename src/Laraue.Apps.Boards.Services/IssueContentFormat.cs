namespace Laraue.Apps.Boards.Services;

/// <summary>
/// An issue's stored <c>Content</c> is normalized to use this as its only line separator - see
/// <see cref="IssueChange{TSelf}.SetContent"/>, which is the single choke point both the web app
/// and Telegram funnel content through. Anything that splits/rejoins issue content by line (e.g.
/// Telegram preview formatting) should reference this instead of hardcoding "\n" itself, so the
/// two stay in sync if this ever changes.
/// </summary>
public static class IssueContentFormat
{
    public const char LineSeparator = '\n';

    public static readonly string LineSeparatorString = LineSeparator.ToString();
}
