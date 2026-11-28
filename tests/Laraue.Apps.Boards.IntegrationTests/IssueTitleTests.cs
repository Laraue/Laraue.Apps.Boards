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

    [Theory]
    [InlineData("[Sim] Magti", "[Sim] Magti", null)]
    [InlineData("**Fix**   the `login`", "**Fix** the `login`", null)]
    [InlineData("Fix the login.", "Fix the login.", null)]
    [InlineData("Fix the login. Then check the logs.", "Fix the login", "Then check the logs.")]
    [InlineData("Fix the login. **Then** check\nthe logs", "Fix the login", "**Then** check\nthe logs")]
    [InlineData("Fix the login\nThen check the logs", "Fix the login", "Then check the logs")]
    [InlineData("\n# Fix the login\n\n- first\n- second\n\n", "# Fix the login", "- first\n- second")]
    [InlineData("Fix the login\n---\nThen check the logs", "Fix the login", "---\nThen check the logs")]
    [InlineData("Fix the login\r\nThen check the logs", "Fix the login", "Then check the logs")]
    public void Split_ShouldTakeTitleAsWrittenAndLeaveContentWithoutIt_WhenTextHasOne(
        string content,
        string expectedTitle,
        string? expectedContent)
    {
        var split = IssueTitle.Split(content);

        Assert.Equal(new IssueTitleSplit(expectedTitle, expectedContent), split);
    }

    [Theory]
    [InlineData("  \n \t")]
    [InlineData("")]
    [InlineData(null)]
    public void Split_ShouldKeepContent_WhenItHasNoTitle(string? content)
    {
        Assert.Equal(new IssueTitleSplit(string.Empty, content), IssueTitle.Split(content));
    }

    [Fact]
    public void Split_ShouldKeepContent_WhenTitleIsCut()
    {
        var content = string.Join(' ', Enumerable.Repeat("word", 100)) + "\nsecond line";

        var split = IssueTitle.Split(content);

        Assert.EndsWith("word…", split.Title);
        Assert.Equal(content, split.Content);
    }

    [Fact]
    public void Normalize_ShouldNotShortenToFirstSentence_WhenTitleWasTypedByHand()
    {
        Assert.Equal("Fix the login. Then check the logs.", IssueTitle.Normalize("Fix the login.   Then check the logs."));
    }
}
