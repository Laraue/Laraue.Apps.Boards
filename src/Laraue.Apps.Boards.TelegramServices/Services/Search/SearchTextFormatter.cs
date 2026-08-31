using System.Text;
using System.Text.RegularExpressions;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

public static class SearchTextFormatter
{
    private const string ReservedMarkdownV2Characters = "_*[]()~`>#+-=|{}.!\\";

    public static string EscapeMarkdownV2(string text)
    {
        var sb = new StringBuilder(text.Length);
        AppendEscapedMarkdownV2(sb, text);
        return sb.ToString();
    }

    /// <summary>
    /// Same escaping as <see cref="EscapeMarkdownV2"/>, but writes directly into an existing
    /// <see cref="StringBuilder"/> from a span instead of allocating an intermediate string -
    /// for callers (like <see cref="TelegramMarkdownFormatter"/>) building up a larger result
    /// out of many small escaped segments.
    /// </summary>
    public static void AppendEscapedMarkdownV2(StringBuilder sb, ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (ReservedMarkdownV2Characters.IndexOf(c) >= 0)
                sb.Append('\\');
            sb.Append(c);
        }
    }

    // Runs of 3+ of these are almost always a decorative divider (markdown horizontal rule
    // ---/___/***, a box-drawing line ─────, or an em/en-dash rule ————), never real content
    // worth showing in a one-line preview. Requiring 3+ avoids stripping legitimate short runs
    // like "--" in code or "===" in a single comparison operator.
    private static readonly Regex DecorativeRunRegex = new(
        @"[-_=~*─━│┃—–]{3,}",
        RegexOptions.Compiled);

    /// <summary>Strips decorative separator runs (horizontal rules, box-drawing lines) entirely.</summary>
    public static string RemoveDecorativeRuns(string content) =>
        DecorativeRunRegex.Replace(content, string.Empty);

    /// <summary>
    /// Cleans raw issue content for use in a preview: strips decorative separator runs, then
    /// collapses all whitespace (including the gaps left behind by the removed separators)
    /// down to single spaces.
    /// </summary>
    public static string CleanForPreview(string content) =>
        NormalizeWhitespace(RemoveDecorativeRuns(content));

    public static string NormalizeWhitespace(string content)
    {
        var sb = new StringBuilder(content.Length);
        var lastWasSpace = false;

        foreach (var c in content)
        {
            if (c is '\r' or '\n' or '\t' or ' ')
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        return sb.ToString().Trim();
    }

    // Mathematical Alphanumeric Symbols block: these are distinct Unicode codepoints for bold
    // letters/digits, not a formatting instruction, so they render bold in *any* plain-text
    // surface — including Telegram's inline result "description" field, which ignores MarkdownV2.
    // Codepoints are outside the Basic Multilingual Plane, hence char.ConvertFromUtf32.
    private const int BoldUpperBase = 0x1D400; // 𝐀
    private const int BoldLowerBase = 0x1D41A; // 𝐚
    private const int BoldDigitBase = 0x1D7CE; // 𝟎

    /// <summary>
    /// Converts ASCII letters and digits to their Unicode bold equivalents. Everything else
    /// (spaces, punctuation, non-ASCII characters) is left untouched — there's no bold variant
    /// for punctuation in this block, and forcing non-ASCII text through it would garble it.
    /// </summary>
    public static string ToUnicodeBold(string text)
    {
        var sb = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            int? codepoint = c switch
            {
                >= 'A' and <= 'Z' => BoldUpperBase + (c - 'A'),
                >= 'a' and <= 'z' => BoldLowerBase + (c - 'a'),
                >= '0' and <= '9' => BoldDigitBase + (c - '0'),
                _ => null
            };

            if (codepoint is { } cp)
            {
                sb.Append(char.ConvertFromUtf32(cp));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}