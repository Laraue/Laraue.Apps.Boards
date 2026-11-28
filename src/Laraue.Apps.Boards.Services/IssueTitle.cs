using System.Text;
using System.Text.RegularExpressions;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Interpreter.Markdown;
using Laraue.Interpreter.Markdown.Body;
using Laraue.Interpreter.Markdown.Body.BlockElements;
using Laraue.Interpreter.Markdown.Body.Blocks;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Derives an issue title: from a text someone typed (<see cref="Split"/>), or from markdown such as an AI reply
/// (<see cref="FromContent"/>). The <c>AddIssueTitle</c> migration backfilled existing issues with a similar
/// rule in SQL (regex based, no word-boundary cut).
/// </summary>
public static partial class IssueTitle
{
    private static readonly MarkdownTranspiler Transpiler = new();

    /// <summary>
    /// Parses the first non-empty, non-rule line of <paramref name="content"/> as markdown and joins its
    /// text nodes (emphasis, headers, quotes and list bullets are dropped, links keep their text),
    /// collapses whitespace, keeps only the first sentence (see <see cref="FirstSentence"/>) and cuts the
    /// result to <see cref="Constraints.MaxTitleLength"/> on a word boundary with an ellipsis. Returns an
    /// empty string for empty content. For markdown that may carry formatting, e.g. an AI reply - a text someone
    /// typed goes through <see cref="Split"/>, which keeps it as written.
    /// </summary>
    public static string FromContent(string? content)
    {
        var firstLine = FirstNonEmptyLine(content);
        if (firstLine.IsEmpty)
            return string.Empty;

        var line = firstLine.Trim().ToString();

        var text = PlainText(line);

        return Normalize(FirstSentence(text));
    }

    /// <summary>
    /// Takes the title from a text someone typed (a Telegram message) and returns the text without it, so a card
    /// doesn't repeat its title in the description. The text is taken as written, not as markdown - a title shows
    /// no markdown, so "[Sim] Magti" stays "[Sim] Magti". The title is the first non-empty line with whitespace
    /// collapsed, its first sentence when it has several (see <see cref="FirstSentence"/>): a one-line text gives
    /// a title and no content, "First. Second." gives "First" and "Second.", several lines give the first one as
    /// the title and the rest as the content. The text stays whole when it has no title or the title had to be
    /// cut to <see cref="Constraints.MaxTitleLength"/> - then it doesn't hold the whole text.
    /// </summary>
    public static IssueTitleSplit Split(string? text)
    {
        if (text is null)
            return new IssueTitleSplit(string.Empty, null);

        var lines = text
            .ReplaceLineEndings(IssueContentFormat.LineSeparatorString)
            .Split(IssueContentFormat.LineSeparator);

        var titleLineIndex = Array.FindIndex(lines, x => !string.IsNullOrWhiteSpace(x));
        if (titleLineIndex < 0)
            return new IssueTitleSplit(string.Empty, text);

        var line = lines[titleLineIndex].Trim();
        var sentence = FirstSentence(Whitespace().Replace(line, " "));
        var title = Normalize(sentence);

        if (sentence.Length > Constraints.MaxTitleLength)
            return new IssueTitleSplit(title, text);

        var rest = new List<string>();
        var nextLines = lines.Skip(titleLineIndex + 1);

        // The rest of the title line after its first sentence, as written.
        if (SentenceEnd().Match(line) is { Success: true } match)
            rest.Add(line[(match.Index + match.Length - 1)..]);
        else
            nextLines = nextLines.SkipWhile(string.IsNullOrWhiteSpace);

        rest.AddRange(nextLines);

        var remainingContent = string.Join(IssueContentFormat.LineSeparator, rest).TrimEnd();

        return new IssueTitleSplit(title, remainingContent.Length > 0 ? remainingContent : null);
    }

    private static string PlainText(string line)
    {
        return Whitespace().Replace(TryGetText(line), " ").Trim();
    }

    /// <summary>
    /// A line with several sentences gives its first one, without the period. A sentence ends at a period
    /// followed by whitespace and more text, so "1.2", "example.com" and a closing period are left alone.
    /// Only for titles taken from content - a title someone typed is never shortened.
    /// </summary>
    private static string FirstSentence(string text)
    {
        var first = SentenceEnd().Match(text) is { Success: true } match ? match.Groups["first"].Value.Trim() : string.Empty;

        return first.Length > 0 ? first : text;
    }

    /// <summary>
    /// Collapses whitespace, trims and cuts <paramref name="title"/> to
    /// <see cref="Constraints.MaxTitleLength"/> on a word boundary with an ellipsis.
    /// </summary>
    public static string Normalize(string? title)
    {
        var text = Whitespace().Replace(title ?? string.Empty, " ").Trim();

        return text.Length <= Constraints.MaxTitleLength
            ? text
            : TextTruncation.Truncate(text, Constraints.MaxTitleLength - 1);
    }

    private static ReadOnlySpan<char> FirstNonEmptyLine(string? content)
    {
        if (content is null)
            return default;

        foreach (var line in content.AsSpan().EnumerateLines())
        {
            if (!line.IsWhiteSpace() && !IsHorizontalRule(line))
                return line;
        }

        return default;
    }

    // "---", "***", "___" - a rule has no text (and "---" alone would be read as an unclosed meta section).
    private static bool IsHorizontalRule(ReadOnlySpan<char> line)
    {
        var trimmed = line.Trim();

        return trimmed.Length >= 3 && trimmed.IndexOfAnyExcept("-*_=") < 0;
    }

    private static string TryGetText(string line)
    {
        try
        {
            var tree = Transpiler.GetTree(line).Tree;
            var builder = new StringBuilder();
            foreach (var block in tree.ContentBlocks)
                AppendText(builder, block);

            return builder.ToString();
        }
        catch (Exception)
        {
            // Markdown the parser can't handle still has a usable first line.
            return line;
        }
    }

    private static void AppendText(StringBuilder builder, MarkdownContentBlock block)
    {
        switch (block)
        {
            case PlainMarkdownContentBlock plain:
                AppendText(builder, plain.Elements);
                break;
            case HeadingMarkdownContentBlock heading:
                AppendText(builder, heading.Elements);
                break;
            case CodeMarkdownContentBlock code:
                AppendText(builder, code.Elements);
                break;
            case BlockquoteContentBlock quote:
                foreach (var elements in quote.Elements)
                    AppendText(builder, elements);
                break;
            case ListBlock list:
                foreach (var row in list.Rows)
                    AppendText(builder, row);
                break;
        }
    }

    private static void AppendText(StringBuilder builder, ListRow row)
    {
        AppendText(builder, row.Elements);
        foreach (var child in row.Children)
            AppendText(builder, child);
    }

    private static void AppendText(StringBuilder builder, IEnumerable<MarkdownContentBlockElement> elements)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case PlainMarkdownContentBlockElement plain:
                    builder.Append(plain.Content);
                    break;
                case BoldMarkdownContentBlockElement bold:
                    AppendText(builder, bold.InnerElements);
                    break;
                case ItalicMarkdownContentBlockElement italic:
                    AppendText(builder, italic.InnerElements);
                    break;
                case StrikethroughMarkdownContentBlockElement strikethrough:
                    AppendText(builder, strikethrough.InnerElements);
                    break;
                case InlineCodeMarkdownContentBlockElement inlineCode:
                    AppendText(builder, inlineCode.InnerElements);
                    break;
                case LinkCodeMarkdownContentBlockElement link:
                    AppendText(builder, link.Link);
                    break;
                case ImageCodeMarkdownContentBlockElement image:
                    builder.Append(image.Alt);
                    break;
            }
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^(?<first>.+?)\.\s+\S")]
    private static partial Regex SentenceEnd();
}

/// <summary>
/// A title taken from content (<see cref="IssueTitle.Split"/>) and the content left without it - null when the
/// title took all of it. <see cref="Title"/> is empty when the content has no text to make a title from.
/// </summary>
public sealed record IssueTitleSplit(string Title, string? Content);
