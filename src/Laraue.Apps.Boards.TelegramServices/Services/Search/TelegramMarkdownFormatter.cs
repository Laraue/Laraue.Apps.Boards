using System.Text;
using System.Text.RegularExpressions;
using Laraue.Apps.Boards.Services;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Converts the markdown subset produced by <c>IAiContentSummarizer</c> and typed by users
/// (headers, bold/italic, inline code, bullet lists) into Telegram MarkdownV2 formatting
/// entities, instead of escaping it into literal text the way <see cref="SearchTextFormatter.EscapeMarkdownV2"/>
/// alone does. This is not a full markdown parser - anything outside this subset (tables, nested
/// lists, links, code blocks, ...) is escaped as literal text, same as before.
///
/// Built entirely on <see cref="ReadOnlySpan{T}"/> slices of the input plus one
/// <see cref="StringBuilder"/> for the output - issue content can be long and this runs on every
/// /save, /info and inline search reply, so it avoids the array/substring allocations a
/// Split+Regex.Matches+Group.Value approach would produce per line and per span.
/// </summary>
public static class TelegramMarkdownFormatter
{
    // Order matters: code spans are matched first so a `**`/`_` inside a code span isn't mistaken
    // for a bold/italic marker. Each alternative requires non-empty, single-line content between
    // its delimiters, so a lone/unpaired marker (e.g. a stray "`" left by fragment windowing) is
    // simply left unmatched and falls through to be escaped as literal text.
    private static readonly Regex InlineSpanRegex = new(
        @"`[^`\r\n]+`|\*\*[^*\r\n]+\*\*|\*[^*\r\n]+\*|_[^_\r\n]+_",
        RegexOptions.Compiled);

    // Telegram's own pre-block fence syntax, so it's used both to detect a fence line and to
    // write it back out unescaped/unmodified (unlike everything else, which gets escaped).
    private const string FenceMarker = "```";

    /// <summary>
    /// <paramref name="startInsideFence"/> is for callers formatting a slice of a larger
    /// document (like <see cref="ContentFragment"/>'s combined Prefix+Match+Suffix span) rather
    /// than the whole thing: pass whatever <see cref="IsInsideFenceAtPosition"/> reports for the
    /// slice's starting position in the full content, so a fence opened before the slice began -
    /// and whose real closing marker happens to fall inside it - is recognized as a close instead
    /// of being mistaken for a second opening (which would wrongly swallow everything after it as
    /// code).
    ///
    /// <paramref name="highlightStart"/>/<paramref name="highlightLength"/> bold-wrap a sub-range
    /// of <paramref name="text"/> (a search match) - a range must be a single call's worth of
    /// text formatted in one pass, not two independently-formatted chunks stitched together with
    /// a separately-escaped highlight in between, because that independence is exactly what let a
    /// fence spanning across the highlight get its open/close markers out of sync (see the
    /// history of bugs this replaced in ContentFragment). A range inside a fence is left
    /// unhighlighted - Telegram doesn't support nesting bold inside a pre/code entity anyway.
    /// </summary>
    public static string ToTelegramMarkdownV2(
        string text,
        bool startInsideFence = false,
        int highlightStart = -1,
        int highlightLength = 0)
    {
        var sb = new StringBuilder(text.Length);
        ReadOnlySpan<char> remaining = text;
        var isFirstLine = true;
        var insideFence = startInsideFence;
        var lineGlobalStart = 0;

        // The posted message is a standalone string - Telegram has no idea a fence was
        // conceptually "already open" before this text started, so recognizing the real closing
        // marker further down (via startInsideFence) isn't enough on its own: without an opening
        // marker of its own, that lone closing "```" is just as invalid as a lone opening one.
        // Synthesize the missing opener so the entity is actually paired in what gets sent.
        if (insideFence)
        {
            sb.Append(FenceMarker).Append(IssueContentFormat.LineSeparator);
        }

        // Manual line-separator splitting instead of string.Split(LineSeparator) - same semantics
        // (a trailing separator still produces a trailing empty line) without allocating the
        // intermediate string[].
        while (true)
        {
            var newlineIndex = remaining.IndexOf(IssueContentFormat.LineSeparator);
            var line = newlineIndex < 0 ? remaining : remaining[..newlineIndex];

            if (!isFirstLine) sb.Append(IssueContentFormat.LineSeparator);
            isFirstLine = false;

            if (insideFence)
            {
                if (line.StartsWith(FenceMarker))
                {
                    sb.Append(FenceMarker);
                    insideFence = false;
                }
                else
                {
                    // Same escaping as inline code (only backtick/backslash) - header/bullet/
                    // bold/italic syntax inside a code block is code, not markdown, and must be
                    // shown verbatim. A highlight range landing here is intentionally ignored.
                    AppendEscapedCode(sb, line);
                }
            }
            else if (line.StartsWith(FenceMarker))
            {
                // Whatever follows the opening marker on this line is the (optional) language
                // tag - Telegram's fence syntax takes it as-is, not as escaped message text.
                sb.Append(FenceMarker).Append(line[FenceMarker.Length..]);
                insideFence = true;
            }
            else
            {
                FormatLine(line, sb, highlightStart - lineGlobalStart, highlightLength);
            }

            if (newlineIndex < 0) break;
            lineGlobalStart += newlineIndex + 1;
            remaining = remaining[(newlineIndex + 1)..];
        }

        // The content ended while still inside a fence - fragment windowing/truncation cut it
        // off (or the source markdown was simply missing its closing marker). Close it here so
        // Telegram's parser never sees an unterminated pre entity; the snippet will just look
        // incomplete, which is an acceptable tradeoff for a preview - the full block is always
        // available via "Open issue".
        if (insideFence)
        {
            sb.Append(IssueContentFormat.LineSeparator).Append(FenceMarker);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Whether <paramref name="position"/> (an absolute index into <paramref name="content"/>)
    /// falls between a fence's opening and closing marker line - used by
    /// <see cref="ContentFragment"/> to tell <see cref="ToTelegramMarkdownV2"/> the correct
    /// starting state when formatting a slice that begins partway through the full content,
    /// rather than always assuming a slice starts outside any fence. Only whole lines that
    /// complete at or before <paramref name="position"/> count - a line straddling it can't
    /// itself be a fence delimiter's open/close toggle that's already taken effect, since
    /// delimiter lines are always slice-worthy on their own.
    /// </summary>
    public static bool IsInsideFenceAtPosition(string content, int position)
    {
        var insideFence = false;
        var lineStart = 0;

        while (lineStart < position)
        {
            var lineEndSearch = content.IndexOf(IssueContentFormat.LineSeparator, lineStart);
            var lineEnd = lineEndSearch < 0 ? content.Length : lineEndSearch;

            if (position <= lineEnd)
            {
                break;
            }

            if (content.AsSpan(lineStart, lineEnd - lineStart).StartsWith(FenceMarker))
            {
                insideFence = !insideFence;
            }

            lineStart = lineEnd + 1;
        }

        return insideFence;
    }

    /// <summary>
    /// True when <paramref name="localIndex"/> falls strictly inside a matched inline span
    /// (code/bold/italic) on <paramref name="line"/> - used by
    /// <see cref="ContentFragment"/> so a fragment window doesn't get cut in the middle of one.
    /// </summary>
    public static bool TryFindEnclosingSpan(ReadOnlySpan<char> line, int localIndex, out int start, out int end)
    {
        foreach (var match in InlineSpanRegex.EnumerateMatches(line))
        {
            if (localIndex > match.Index && localIndex < match.Index + match.Length)
            {
                start = match.Index;
                end = match.Index + match.Length;
                return true;
            }
        }

        start = 0;
        end = 0;
        return false;
    }

    private static void FormatLine(ReadOnlySpan<char> line, StringBuilder sb, int highlightStart, int highlightLength)
    {
        if (TryStripHeaderPrefix(line, out var headerText))
        {
            // Nothing left to show (e.g. a bare "#") - drop the line rather than emit an empty
            // "**" pair. Formatting/escaping never removes non-whitespace content, so checking
            // the raw span here is equivalent to checking the formatted result.
            if (headerText.Length == 0) return;

            var prefixLength = line.Length - headerText.Length;
            sb.Append('*');
            FormatInline(headerText, sb, highlightStart - prefixLength, highlightLength);
            sb.Append('*');
            return;
        }

        if (TryStripBulletPrefix(line, out var bulletText))
        {
            var prefixLength = line.Length - bulletText.Length;
            sb.Append('•').Append(' ');
            FormatInline(bulletText, sb, highlightStart - prefixLength, highlightLength);
            return;
        }

        FormatInline(line, sb, highlightStart, highlightLength);
    }

    // "^(?:#{1,6})[ \t]+" - 1 to 6 '#' followed by required whitespace. More than 6 leading '#'
    // is not a valid header (mirrors CommonMark), so the whole line falls through as plain text.
    private static bool TryStripHeaderPrefix(ReadOnlySpan<char> line, out ReadOnlySpan<char> content)
    {
        var i = 0;
        while (i < line.Length && line[i] == '#') i++;

        if (i is 0 or > 6 || i >= line.Length || (line[i] != ' ' && line[i] != '\t'))
        {
            content = default;
            return false;
        }

        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        content = line[i..];
        return true;
    }

    // "^[ \t]*[-*][ \t]+" - optional leading whitespace, one bullet marker, required whitespace.
    private static bool TryStripBulletPrefix(ReadOnlySpan<char> line, out ReadOnlySpan<char> content)
    {
        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;

        if (i >= line.Length || (line[i] != '-' && line[i] != '*'))
        {
            content = default;
            return false;
        }
        i++;

        if (i >= line.Length || (line[i] != ' ' && line[i] != '\t'))
        {
            content = default;
            return false;
        }
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;

        content = line[i..];
        return true;
    }

    private static void FormatInline(ReadOnlySpan<char> text, StringBuilder sb, int highlightStart, int highlightLength)
    {
        // A highlight range fully contained in this line's text: format the parts before/after
        // it normally, and bold-wrap the highlighted part verbatim-escaped, with no further
        // inline-span interpretation inside it (matching the simple "just escape and bold the
        // raw match text" behavior this always had, back when it wasn't line-aware yet).
        if (highlightLength > 0 && highlightStart >= 0 && highlightStart + highlightLength <= text.Length)
        {
            FormatInlineSpans(text[..highlightStart], sb);
            sb.Append('*');
            SearchTextFormatter.AppendEscapedMarkdownV2(sb, text.Slice(highlightStart, highlightLength));
            sb.Append('*');
            FormatInlineSpans(text[(highlightStart + highlightLength)..], sb);
            return;
        }

        FormatInlineSpans(text, sb);
    }

    private static void FormatInlineSpans(ReadOnlySpan<char> text, StringBuilder sb)
    {
        var offset = 0;

        foreach (var match in InlineSpanRegex.EnumerateMatches(text))
        {
            SearchTextFormatter.AppendEscapedMarkdownV2(sb, text[offset..match.Index]);

            var span = text.Slice(match.Index, match.Length);
            if (span[0] == '`')
            {
                sb.Append('`');
                AppendEscapedCode(sb, span[1..^1]);
                sb.Append('`');
            }
            else if (span[1] == '*') // starts with "**" -> bold
            {
                sb.Append('*');
                SearchTextFormatter.AppendEscapedMarkdownV2(sb, span[2..^2]);
                sb.Append('*');
            }
            else // starts with a single '*' or '_' -> italic
            {
                sb.Append('_');
                SearchTextFormatter.AppendEscapedMarkdownV2(sb, span[1..^1]);
                sb.Append('_');
            }

            offset = match.Index + match.Length;
        }

        SearchTextFormatter.AppendEscapedMarkdownV2(sb, text[offset..]);
    }

    // Inside a Telegram MarkdownV2 code span, only backtick and backslash need escaping - running
    // the regular reserved-character escaping over code content would corrupt it (e.g. treat a
    // path's "." or "/" neighbours as needing escaping when they don't inside a code span).
    private static void AppendEscapedCode(StringBuilder sb, ReadOnlySpan<char> code)
    {
        foreach (var c in code)
        {
            if (c is '\\' or '`') sb.Append('\\');
            sb.Append(c);
        }
    }
}
