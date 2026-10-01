namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Cheap, dependency-free text truncation shared by anything that shortens user text - issue titles
/// (<see cref="IssueTitle"/>) and MCP tool responses (<c>IssueTools</c>, <c>Laraue.Apps.Boards.McpHost</c>) build
/// on this rather than each hard-cutting text mid-word independently. It only knows about plain text.
/// </summary>
public static class TextTruncation
{
    private const int DefaultMaxWordBoundaryExpansion = 20;

    /// <summary>
    /// Finds the best index at or before <paramref name="cut"/> to end a truncation at without
    /// splitting a word - walks back to the nearest preceding whitespace, but gives up and
    /// returns <paramref name="cut"/> unchanged if no whitespace is found within
    /// <paramref name="maxBoundaryExpansion"/> characters (one very long "word" - a URL, a hash,
    /// minified code - shouldn't be allowed to shrink the truncation arbitrarily far back).
    /// </summary>
    public static int TrimToWordBoundary(string content, int cut, int maxBoundaryExpansion = DefaultMaxWordBoundaryExpansion)
    {
        var limit = Math.Max(0, cut - maxBoundaryExpansion);
        var i = cut;
        while (i > limit && !char.IsWhiteSpace(content[i - 1]))
        {
            i--;
        }

        return i > limit ? i : cut;
    }

    /// <summary>
    /// Truncates <paramref name="content"/> to at most <paramref name="maxLength"/> characters,
    /// cutting at a word boundary via <see cref="TrimToWordBoundary"/> and appending "…" if it
    /// was actually shortened. Returns <paramref name="content"/> unchanged if it already fits.
    /// </summary>
    public static string Truncate(string content, int maxLength, int maxBoundaryExpansion = DefaultMaxWordBoundaryExpansion)
    {
        if (content.Length <= maxLength)
            return content;

        var cut = TrimToWordBoundary(content, maxLength, maxBoundaryExpansion);

        return content[..cut].TrimEnd() + "…";
    }
}
