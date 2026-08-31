using Laraue.Apps.Boards.TelegramServices.Services.Search;

namespace Laraue.Apps.Boards.IntegrationTests;

public class TelegramMarkdownFormatterTests
{
    [Fact]
    public void ToTelegramMarkdownV2_ShouldRenderHeaderAsBoldLine()
    {
        var result = TelegramMarkdownFormatter.ToTelegramMarkdownV2("# Fix incorrect endpoint");

        Assert.Equal("*Fix incorrect endpoint*", result);
    }

    [Fact]
    public void ToTelegramMarkdownV2_ShouldRenderBulletLineWithBulletGlyph()
    {
        var result = TelegramMarkdownFormatter.ToTelegramMarkdownV2("- a separate endpoint is needed");

        Assert.Equal("• a separate endpoint is needed", result);
    }

    [Fact]
    public void ToTelegramMarkdownV2_ShouldConvertInlineCodeSpan()
    {
        var result = TelegramMarkdownFormatter.ToTelegramMarkdownV2("`/spaces/BRD/members` was used previously.");

        Assert.Equal("`/spaces/BRD/members` was used previously\\.", result);
    }

    [Fact]
    public void ToTelegramMarkdownV2_ShouldConvertDoubleAsteriskBoldToSingleAsterisk()
    {
        var result = TelegramMarkdownFormatter.ToTelegramMarkdownV2("this is **important** text");

        Assert.Equal("this is *important* text", result);
    }

    [Fact]
    public void ToTelegramMarkdownV2_ShouldConvertUnderscoreItalic()
    {
        var result = TelegramMarkdownFormatter.ToTelegramMarkdownV2("this is _emphasized_ text");

        Assert.Equal("this is _emphasized_ text", result);
    }

    [Fact]
    public void ToTelegramMarkdownV2_ShouldEscapeReservedCharactersOutsideRecognizedSyntax()
    {
        var result = TelegramMarkdownFormatter.ToTelegramMarkdownV2("100% done (v1.2)");

        Assert.Equal("100% done \\(v1\\.2\\)", result);
    }

    [Fact]
    public void ToTelegramMarkdownV2_ShouldEscapeUnpairedDelimiterAsLiteralText()
    {
        var result = TelegramMarkdownFormatter.ToTelegramMarkdownV2("stray ` backtick");

        Assert.Equal("stray \\` backtick", result);
    }

    [Fact]
    public void ToTelegramMarkdownV2_ShouldHandleMultipleLinesIndependently()
    {
        var result = TelegramMarkdownFormatter.ToTelegramMarkdownV2("# Title\n\n- one\n- two with `code`");

        Assert.Equal("*Title*\n\n• one\n• two with `code`", result);
    }

    [Fact]
    public void ToTelegramMarkdownV2_ShouldRenderFencedCodeBlockVerbatim_WithoutApplyingInlineOrLineFormatting()
    {
        // Regression test: the raw content of a fenced block (headers, bullets, asterisks - all
        // of it) must pass through untouched except for backtick/backslash escaping, since it's
        // code, not markdown to be reformatted.
        var result = TelegramMarkdownFormatter.ToTelegramMarkdownV2(
            "```\n" +
            "enum EpicStatus\n" +
            "{\n" +
            "  New,\n" +
            "  # not a header\n" +
            "  - not a bullet *not bold*\n" +
            "}\n" +
            "```");

        Assert.Equal(
            "```\n" +
            "enum EpicStatus\n" +
            "{\n" +
            "  New,\n" +
            "  # not a header\n" +
            "  - not a bullet *not bold*\n" +
            "}\n" +
            "```",
            result);
    }

    [Fact]
    public void ToTelegramMarkdownV2_ShouldPreserveLanguageTagOnOpeningFence()
    {
        var result = TelegramMarkdownFormatter.ToTelegramMarkdownV2("```csharp\nvar x = 1;\n```");

        Assert.Equal("```csharp\nvar x = 1;\n```", result);
    }

    [Fact]
    public void ToTelegramMarkdownV2_ShouldAutoCloseFence_WhenContentEndsWithoutClosingMarker()
    {
        // Regression test: this is what happens when fragment truncation cuts off content in the
        // middle of a fenced block (see the BRD-161 case) - close the pre entity ourselves so
        // Telegram's parser never sees an unterminated one, instead of shipping broken raw
        // "```" characters as plain text in the middle of the message.
        var result = TelegramMarkdownFormatter.ToTelegramMarkdownV2("```\nenum EpicStatus\n{\n  New,");

        Assert.Equal("```\nenum EpicStatus\n{\n  New,\n```", result);
    }

    [Fact]
    public void ToTelegramMarkdownV2_ShouldResumeNormalFormatting_AfterFenceCloses()
    {
        var result = TelegramMarkdownFormatter.ToTelegramMarkdownV2("```\ncode\n```\n# Title after");

        Assert.Equal("```\ncode\n```\n*Title after*", result);
    }

    [Theory]
    [InlineData(4, false)] // on the opening backtick itself - not strictly inside
    [InlineData(5, true)] // just past the opening backtick - inside
    [InlineData(24, true)] // on the closing backtick itself - still inside (end is exclusive)
    [InlineData(25, false)] // just past the closing backtick - outside the span
    public void TryFindEnclosingSpan_ShouldOnlyMatchIndexesStrictlyInsideTheSpan(int index, bool expectMatch)
    {
        const string line = "see `/spaces/BRD/members` here";

        var found = TelegramMarkdownFormatter.TryFindEnclosingSpan(line, index, out var start, out var end);

        Assert.Equal(expectMatch, found);
        if (found)
        {
            Assert.Equal("`/spaces/BRD/members`", line[start..end]);
        }
    }
}
