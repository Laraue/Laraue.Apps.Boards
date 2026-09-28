using Laraue.Apps.Boards.Services;

namespace Laraue.Apps.Boards.IntegrationTests;

public class UserInitialsTests
{
    [Theory]
    [InlineData("Ivan Petrov", "IP")]
    [InlineData("ivan  petrov sidorov", "IP")]
    [InlineData(" Ivan ", "IV")]
    [InlineData("I", "I")]
    public void FromDisplayName_ShouldDeriveInitials_Always(string displayName, string expected)
    {
        Assert.Equal(expected, UserInitials.FromDisplayName(displayName));
    }
}
