using Laraue.Apps.Boards.Services;

namespace Laraue.Apps.Boards.IntegrationTests;

public class IssueTitleTests
{
    [Theory]
    [InlineData("Fix the login", "Fix the login")]
    [InlineData("\n\n  Fix the login\nsecond line", "Fix the login")]
    [InlineData("# **Fix** the `login`", "Fix the login")]
    [InlineData("> Fix   the\tlogin", "Fix the login")]
    [InlineData("1. Fix the login", "Fix the login")]
    [InlineData("---", "")]
    [InlineData("---\n***\nFix the login", "Fix the login")]
    [InlineData("- [Fix the login](https://example.com/x) now", "Fix the login now")]
    [InlineData("_Fix_ snake_case", "Fix snake_case")]
    [InlineData("Fix the login. Then check the logs.", "Fix the login")]
    [InlineData("Fix the login.", "Fix the login.")]
    [InlineData("Release 1.2 of example.com is out", "Release 1.2 of example.com is out")]
    [InlineData("**Fix the login.**   *Then* more\nsecond line. third", "Fix the login")]
    [InlineData(". starts with a period", ". starts with a period")]
    [InlineData("", "")]
    [InlineData("   \n \n", "")]
    [InlineData(null, "")]
    public void FromContent_ShouldSanitizeFirstLine_WhenContentHasMarkdown(string? content, string expected)
    {
        Assert.Equal(expected, IssueTitle.FromContent(content));
    }

    [Fact]
    public void FromContent_ShouldCutOnWordBoundaryWithEllipsis_WhenFirstLineIsTooLong()
    {
        var content = string.Join(' ', Enumerable.Repeat("word", 100));

        var title = IssueTitle.FromContent(content);

        Assert.True(title.Length <= 256);
        Assert.EndsWith("word…", title);
    }

    [Fact]
    public void FromContent_ShouldKeepLine_WhenItIsExactlyMaxLength()
    {
        var content = new string('a', 256);

        Assert.Equal(content, IssueTitle.FromContent(content));
    }

    [Fact]
    public void Normalize_ShouldNotShortenToFirstSentence_WhenTitleWasTypedByHand()
    {
        Assert.Equal("Fix the login. Then check the logs.", IssueTitle.Normalize("Fix the login.   Then check the logs."));
    }
}
