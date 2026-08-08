using System.Text;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

public readonly record struct ContentFragment(
    string Prefix,
    string Match,
    string Suffix,
    bool TruncatedStart,
    bool TruncatedEnd)
{
    private const int FallbackLength = 500;

    // How far we're willing to grow a window past its budget to land on a whitespace
    // boundary instead of slicing through a word. Bounded so one pathological long
    // "word" (a URL, a hash, minified code) can't blow the fragment size up unbounded.
    private const int MaxWordBoundaryExpansion = 20;

    /// <summary>
    /// Finds the first occurrence of <paramref name="searchText"/> in <paramref name="content"/>
    /// and returns a window of text around it. Falls back to a plain start-of-content
    /// truncation when there's no search text or no match is found (e.g. filter-only queries,
    /// or edge cases where DB collation matched something string comparison here doesn't).
    /// </summary>
    public static ContentFragment Extract(string content, string searchText, int contextChars)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return FallbackTruncate(content);
        }

        var matchIndex = content.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
        if (matchIndex < 0)
        {
            return FallbackTruncate(content);
        }

        var matchEnd = matchIndex + searchText.Length;

        var windowStart = ExpandStartToWordBoundary(content, Math.Max(0, matchIndex - contextChars));
        var windowEnd = ExpandEndToWordBoundary(content, Math.Min(content.Length, matchEnd + contextChars));

        return new ContentFragment(
            Prefix: content[windowStart..matchIndex].TrimStart(),
            Match: content[matchIndex..matchEnd],
            Suffix: content[matchEnd..windowEnd].TrimEnd(),
            TruncatedStart: windowStart > 0,
            TruncatedEnd: windowEnd < content.Length);
    }

    private static ContentFragment FallbackTruncate(string content)
    {
        if (content.Length <= FallbackLength)
        {
            return new ContentFragment(content, string.Empty, string.Empty, false, false);
        }

        // Shrink back to the nearest earlier whitespace instead of slicing mid-word.
        var cut = TrimEndToWordBoundary(content, FallbackLength);

        return new ContentFragment(
            Prefix: content[..cut].TrimEnd(),
            Match: string.Empty,
            Suffix: string.Empty,
            TruncatedStart: false,
            TruncatedEnd: true);
    }

    /// <summary>Moves a window-start index left to the previous whitespace, so the prefix doesn't begin mid-word.</summary>
    private static int ExpandStartToWordBoundary(string content, int start)
    {
        if (start <= 0) return 0;

        var limit = Math.Max(0, start - MaxWordBoundaryExpansion);
        var i = start;
        while (i > limit && !char.IsWhiteSpace(content[i - 1]))
        {
            i--;
        }
        return i;
    }

    /// <summary>Moves a window-end index right to the next whitespace, so the suffix doesn't end mid-word.</summary>
    private static int ExpandEndToWordBoundary(string content, int end)
    {
        if (end >= content.Length) return content.Length;

        var limit = Math.Min(content.Length, end + MaxWordBoundaryExpansion);
        var i = end;
        while (i < limit && !char.IsWhiteSpace(content[i]))
        {
            i++;
        }
        return i;
    }

    /// <summary>
    /// Shrinks a cut index left to the previous whitespace, within a small tolerance,
    /// so a plain truncation (no match) doesn't end mid-word either. Unlike the expand
    /// helpers above, this only shrinks — used where growing past the budget isn't wanted.
    /// </summary>
    private static int TrimEndToWordBoundary(string content, int cut)
    {
        var limit = Math.Max(0, cut - MaxWordBoundaryExpansion);
        var i = cut;
        while (i > limit && !char.IsWhiteSpace(content[i - 1]))
        {
            i--;
        }
        // If no whitespace was found within tolerance (one very long word), just keep the
        // original cut rather than over-shrinking the preview.
        return i > limit ? i : cut;
    }

    // Telegram silently truncates InlineQueryResultArticle.Description past 256 characters,
    // with no regard for word boundaries or where the highlighted match sits. The window
    // budget above is sized to stay well under this, but this is a hard backstop so a future
    // change to that budget (or an unusually long match) can never silently regress into
    // Telegram doing its own uncontrolled cut on top of ours.
    private const int TelegramDescriptionMaxLength = 256;

    public string ToPlainText()
    {
        var sb = new StringBuilder();
        if (TruncatedStart) sb.Append('…');
        sb.Append(Prefix);
        sb.Append(Match.Length > 0 ? SearchTextFormatter.ToUnicodeBold(Match) : Match);
        sb.Append(Suffix);
        if (TruncatedEnd) sb.Append('…');

        var text = sb.ToString();
        if (text.Length <= TelegramDescriptionMaxLength)
        {
            return text;
        }

        var cut = TelegramDescriptionMaxLength - 1;
        // Don't split a surrogate pair (used by the Unicode bold characters) in half.
        if (char.IsLowSurrogate(text[cut]))
        {
            cut--;
        }

        return text[..cut].TrimEnd() + "…";
    }

    public string ToMarkdownV2()
    {
        var sb = new StringBuilder();
        if (TruncatedStart) sb.Append('…');
        sb.Append(SearchTextFormatter.EscapeMarkdownV2(Prefix));

        if (Match.Length > 0)
        {
            sb.Append('*');
            sb.Append(SearchTextFormatter.EscapeMarkdownV2(Match));
            sb.Append('*');
        }

        sb.Append(SearchTextFormatter.EscapeMarkdownV2(Suffix));
        if (TruncatedEnd) sb.Append('…');
        return sb.ToString();
    }
}