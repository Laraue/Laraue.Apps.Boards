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
}
