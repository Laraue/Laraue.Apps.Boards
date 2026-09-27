using Laraue.Apps.Boards.Services;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Boards.IntegrationTests;

public class IssueKeyTests
{
    [Theory]
    [InlineData("BRD-1", "BRD", 1)]
    [InlineData("BRD-123456789", "BRD", 123456789)]
    [InlineData(" BRD-42 ", "BRD", 42)]
    public void Constructor_ShouldParseKey_WhenFormatIsValid(string key, string spaceKey, int number)
    {
        var issueKey = new IssueKey(key);

        Assert.Equal(spaceKey, issueKey.SpaceKey);
        Assert.Equal(number, issueKey.Number);
    }

    [Theory]
    [InlineData("XBRD-1")]
    [InlineData("BRD-1234567890")]
    [InlineData("BR-1")]
    [InlineData("BRD-1x")]
    [InlineData("BRD 1")]
    [InlineData("see BRD-1")]
    [InlineData("")]
    public void Constructor_ShouldThrowBadRequest_WhenKeyIsNotExactlyAnIssueKey(string key)
    {
        Assert.Throws<BadRequestException>(() => new IssueKey(key));
    }
}
