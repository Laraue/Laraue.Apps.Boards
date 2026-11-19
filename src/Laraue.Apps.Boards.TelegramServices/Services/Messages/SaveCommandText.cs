namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

/// <summary>
/// Reads what a /save or /aisave command says: its title and whether it asks the AI to rewrite the text.
/// </summary>
public static class SaveCommandText
{
    /// <summary>
    /// Everything after the first space in "/save Some title" -&gt; "Some title" (also strips a bot @mention, e.g.
    /// "/save@mybot Some title"). Null when the command was sent bare.
    /// </summary>
    public static string? ExtractTitle(string? commandText)
    {
        if (string.IsNullOrEmpty(commandText))
            return null;

        var spaceIndex = commandText.IndexOf(' ');
        if (spaceIndex < 0)
            return null;

        // Trim in span-space first so a bare "/save " (no real title) doesn't allocate at all, and a title with
        // surrounding whitespace only allocates once, for the final string.
        var title = commandText.AsSpan(spaceIndex + 1).Trim();
        return title.IsEmpty ? null : title.ToString();
    }

    /// <summary>
    /// "/aisave", with or without a title or a bot @mention.
    /// </summary>
    public static bool IsAiSave(string? commandText)
    {
        if (string.IsNullOrEmpty(commandText))
            return false;

        var name = commandText.AsSpan();
        var end = name.IndexOfAny(' ', '@');

        return (end < 0 ? name : name[..end]).SequenceEqual(TelegramRoutes.Commands.AiSave);
    }
}
