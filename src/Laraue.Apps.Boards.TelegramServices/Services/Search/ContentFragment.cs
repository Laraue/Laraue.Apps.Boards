using System.Text;
using Laraue.Apps.Boards.Services;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

public readonly record struct ContentFragment(
    string Prefix,
    string Match,
    string Suffix,
    bool TruncatedStart,
    bool TruncatedEnd,
    bool StartsInsideFence = false)
{
    private const int FallbackLength = 500;

    // How far we're willing to grow a window past its budget to land on a whitespace
    // boundary instead of slicing through a word. Bounded so one pathological long
    // "word" (a URL, a hash, minified code) can't blow the fragment size up unbounded.
    private const int MaxWordBoundaryExpansion = 20;

    // How far we're willing to grow a window past its budget to clear a whole inline markdown
    // span (code/bold/italic) instead of cutting it in half - see ExpandToMarkdownSpanBoundary.
    // Bounded so a marker that's never actually closed (malformed markdown) can't blow the
    // fragment size up unbounded.
    private const int MaxMarkdownSpanExpansion = 300;

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
        windowStart = ExpandToMarkdownSpanBoundary(content, windowStart, expandForward: false);

        var windowEnd = ExpandEndToWordBoundary(content, Math.Min(content.Length, matchEnd + contextChars));
        windowEnd = ExpandToMarkdownSpanBoundary(content, windowEnd, expandForward: true);

        return new ContentFragment(
            Prefix: content[windowStart..matchIndex].TrimStart(),
            Match: content[matchIndex..matchEnd],
            Suffix: content[matchEnd..windowEnd].TrimEnd(),
            TruncatedStart: windowStart > 0,
            TruncatedEnd: windowEnd < content.Length,
            // ToMarkdownV2 formats Prefix+Match+Suffix as one continuous pass rather than
            // independently - without this, a fence opened earlier in the full content (outside
            // this window) would be invisible to that pass, and its real closing marker (wherever
            // it falls within the window) would be misread as a fresh opening instead.
            StartsInsideFence: TelegramMarkdownFormatter.IsInsideFenceAtPosition(content, windowStart));
    }

    private static ContentFragment FallbackTruncate(string content)
    {
        if (content.Length <= FallbackLength)
        {
            return new ContentFragment(content, string.Empty, string.Empty, false, false);
        }

        // Shrink back to the nearest earlier whitespace instead of slicing mid-word.
        var cut = TrimEndToWordBoundary(content, FallbackLength);
        cut = ExpandToMarkdownSpanBoundary(content, cut, expandForward: true);

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

    /// <summary>
    /// <paramref name="highlightMatch"/> bolds <see cref="Match"/> - only appropriate while this
    /// text is still a search-result preview the user is picking between (the inline query
    /// dropdown). Once a result is selected, its <c>InputTextMessageContent</c> is what actually
    /// gets posted to the chat - at that point the search term is no longer meaningful context
    /// for anyone reading the message, so callers building that posted text should pass false.
    /// </summary>
    public string ToMarkdownV2(bool highlightMatch = true)
    {
        var sb = new StringBuilder();
        if (TruncatedStart) sb.Append('…');

        // Prefix, Match and Suffix are formatted as a single continuous span rather than as
        // three independently-formatted-then-concatenated pieces. Independent formatting means
        // each piece decides its own fence open/close in isolation - which breaks the moment a
        // real fence spans across a piece boundary (its open and close end up tracked by two
        // formatter calls that don't know about each other, so they can disagree about whether
        // the fence is still open, corrupting the entities Telegram sees). One pass keeps fence
        // state - and the match highlight, via highlightStart/highlightLength - consistent
        // throughout. The highlight is silently dropped if it lands inside a fence: Telegram
        // doesn't support nesting bold inside a pre/code entity anyway.
        var combined = Prefix + Match + Suffix;
        var highlightLength = highlightMatch ? Match.Length : 0;

        sb.Append(TelegramMarkdownFormatter.ToTelegramMarkdownV2(combined, StartsInsideFence, Prefix.Length, highlightLength));

        if (TruncatedEnd) sb.Append('…');
        return sb.ToString();
    }

    /// <summary>
    /// If <paramref name="index"/> falls strictly inside a matched inline markdown span (code/
    /// bold/italic) on its line, pushes it to that span's far edge - forward past the closing
    /// marker when <paramref name="expandForward"/>, back before the opening marker otherwise -
    /// so <see cref="ToMarkdownV2"/> never has to render half a span. Bounded by
    /// <see cref="MaxMarkdownSpanExpansion"/>; leaves <paramref name="index"/> untouched if the
    /// span is bigger than that or isn't fully closed within reach.
    /// </summary>
    private static int ExpandToMarkdownSpanBoundary(string content, int index, bool expandForward)
    {
        var lineStart = content.LastIndexOf(IssueContentFormat.LineSeparator, Math.Max(0, index - 1)) + 1;
        var lineEndSearch = content.IndexOf(IssueContentFormat.LineSeparator, index);
        var lineEnd = lineEndSearch < 0 ? content.Length : lineEndSearch;

        var line = content.AsSpan(lineStart, lineEnd - lineStart);
        var localIndex = index - lineStart;

        if (!TelegramMarkdownFormatter.TryFindEnclosingSpan(line, localIndex, out var spanStart, out var spanEnd))
            return index;

        var target = expandForward ? lineStart + spanEnd : lineStart + spanStart;
        var distance = expandForward ? target - index : index - target;

        return distance <= MaxMarkdownSpanExpansion ? target : index;
    }
}