using Laraue.Apps.Boards.TelegramServices.Services.Search;

namespace Laraue.Apps.Boards.IntegrationTests;

public class ContentFragmentTests
{
    [Fact]
    public void ToMarkdownV2_ShouldRenderHeaderBulletsAndInlineCode_WhenNoSearchMatch()
    {
        const string content =
            "# Fix incorrect endpoint when selecting user\n" +
            "\n" +
            "- `/spaces/BRD/members` was used previously.\n" +
            "- a separate endpoint is needed";

        var fragment = ContentFragment.Extract(content, searchText: string.Empty, contextChars: 70);

        Assert.Equal(
            "*Fix incorrect endpoint when selecting user*\n\n" +
            "• `/spaces/BRD/members` was used previously\\.\n" +
            "• a separate endpoint is needed",
            fragment.ToMarkdownV2());
    }

    [Fact]
    public void ToMarkdownV2_ShouldNotSplitInlineCodeSpan_WhenFallbackTruncationWouldCutInsideIt()
    {
        // FallbackLength is 500. The code span itself has no whitespace, so the natural
        // word-boundary cut at 500 has nothing to latch onto and lands mid-span - assert the
        // whole span survives instead of being cut in half.
        var padding = new string('a', 400);
        var codePath = string.Concat(Enumerable.Repeat("segment/", 15));
        var content = $"{padding} `{codePath}` end";

        var fragment = ContentFragment.Extract(content, searchText: string.Empty, contextChars: 70);

        Assert.True(fragment.TruncatedEnd);
        Assert.Contains($"`{codePath}`", fragment.ToMarkdownV2());
    }

    [Fact]
    public void ToMarkdownV2_ShouldNotSplitInlineCodeSpan_WhenSearchWindowWouldCutInsideIt()
    {
        var prefixPadding = new string('a', 100);
        var content = $"{prefixPadding} `/spaces/BRD/members` MATCHTEXT suffix";

        var fragment = ContentFragment.Extract(content, searchText: "MATCHTEXT", contextChars: 5);

        Assert.Contains("`/spaces/BRD/members`", fragment.ToMarkdownV2());
    }

    [Fact]
    public void ToMarkdownV2_ShouldBoldTheMatch_WhenHighlightMatchIsTrue()
    {
        var fragment = ContentFragment.Extract("before MATCHTEXT after", "MATCHTEXT", contextChars: 20);

        Assert.Equal("before *MATCHTEXT* after", fragment.ToMarkdownV2());
        Assert.Equal("before *MATCHTEXT* after", fragment.ToMarkdownV2(highlightMatch: true));
    }

    [Fact]
    public void ToMarkdownV2_ShouldNotBoldTheMatch_WhenHighlightMatchIsFalse()
    {
        // Regression test: this is the text actually posted to the chat once an inline search
        // result is selected. Bolding the search term there is meaningless noise that stays
        // baked into the message forever - only the dropdown preview should highlight it.
        var fragment = ContentFragment.Extract("before MATCHTEXT after", "MATCHTEXT", contextChars: 20);

        Assert.Equal("before MATCHTEXT after", fragment.ToMarkdownV2(highlightMatch: false));
    }

    [Fact]
    public void ToMarkdownV2_ShouldFormatAsOneContinuousBlock_WhenMatchItselfIsInsideAFence()
    {
        // Regression test: searching for a term that matches text *inside* a fenced code block
        // (e.g. "Done" in the enum below) used to crash Telegram's parser. Prefix and Suffix are
        // normally formatted independently, each responsible for its own fence open/close -
        // Prefix would synthesize a closing "```" since it never saw the real one, while Suffix
        // (correctly) wouldn't re-open one it thought was already open. Telegram would then treat
        // the text after Prefix's synthetic close as plain, where the block's own "}" - only
        // escaped as code content, not as plain text - is an invalid, unescaped character.
        const string content =
            "```\n" +
            "enum EpicStatus\n" +
            "{\n" +
            "  New,\n" +
            "  InProgress,\n" +
            "  Done,\n" +
            "}\n" +
            "```\n" +
            "\n" +
            "2. API that will change the status";

        var fragment = ContentFragment.Extract(content, searchText: "Done", contextChars: 1000);

        Assert.Equal(
            "```\n" +
            "enum EpicStatus\n" +
            "{\n" +
            "  New,\n" +
            "  InProgress,\n" +
            "  Done,\n" +
            "}\n" +
            "```\n" +
            "\n" +
            "2\\. API that will change the status",
            fragment.ToMarkdownV2());

        // Whatever the exact text, the fence markers in the final message must always pair up -
        // an odd count is exactly what triggers Telegram's "can't parse entities" error.
        Assert.Equal(0, System.Text.RegularExpressions.Regex.Matches(fragment.ToMarkdownV2(), "```").Count % 2);
    }

    [Fact]
    public void ToMarkdownV2_ShouldRecognizeRealClosingFence_WhenWindowStartsInsideIt()
    {
        // Regression test (BRD-161): the window starts *inside* a real fenced code block whose
        // opening marker isn't included in the window at all - only its real closing "```" is.
        // Without StartsInsideFence, that "```" gets mistaken for a brand new opening, swallowing
        // everything after it (here, "find ") as code instead of recognizing it as the block's
        // actual close. And recognizing it as a close isn't enough by itself either: the posted
        // message is a standalone string with no memory of "before the window", so a synthetic
        // opening marker has to be added too, or Telegram sees a lone, unpaired "```".
        const string content = "```\nCODE\n```\n\nfind return here";

        var fragment = ContentFragment.Extract(content, searchText: "ret", contextChars: 13);

        Assert.Equal("…```\nCODE\n```\n\nfind *ret*urn here", fragment.ToMarkdownV2());
    }
}
