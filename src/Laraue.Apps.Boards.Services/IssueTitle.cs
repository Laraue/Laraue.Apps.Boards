using System.Text;
using System.Text.RegularExpressions;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Interpreter.Markdown;
using Laraue.Interpreter.Markdown.Body;
using Laraue.Interpreter.Markdown.Body.BlockElements;
using Laraue.Interpreter.Markdown.Body.Blocks;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Derives an issue title from free-form markdown content. The <c>AddIssueTitle</c> migration
/// backfills existing issues with a similar rule in SQL (regex based, no word-boundary cut).
/// </summary>
public static partial class IssueTitle
{
    private static readonly MarkdownTranspiler Transpiler = new();

    /// <summary>
    /// Parses the first non-empty, non-rule line of <paramref name="content"/> as markdown and joins its
    /// text nodes (emphasis, headers, quotes and list bullets are dropped, links keep their text),
    /// collapses whitespace and cuts the result to <see cref="Constraints.MaxTitleLength"/> on a
    /// word boundary with an ellipsis. Returns an empty string for empty content.
    /// </summary>
    public static string FromContent(string? content)
    {
        var firstLine = FirstNonEmptyLine(content);
        if (firstLine.IsEmpty)
            return string.Empty;

        var line = firstLine.Trim().ToString();
        var text = TryGetText(line);
        text = Whitespace().Replace(text, " ").Trim();

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
}
