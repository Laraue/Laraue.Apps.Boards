using Laraue.Apps.Boards.Services;

namespace Laraue.Apps.Boards.IntegrationTests;

public class TelegramMessageUrlBuilderTests
{
    [Theory]
    // Public supergroup: its @username.
    [InlineData(-1001234567890L, 42, "my_group", "https://t.me/my_group/42")]
    // Private supergroup: the id without "-100".
    [InlineData(-1001234567890L, 42, null, "https://t.me/c/1234567890/42")]
    [InlineData(-1001234567890L, 42, "", "https://t.me/c/1234567890/42")]
    // Basic group: Telegram has no message links, even with a (stale) username.
    [InlineData(-123456789L, 42, null, null)]
    [InlineData(-123456789L, 42, "my_group", null)]
    // Private chat with the bot.
    [InlineData(123456789L, 42, null, null)]
    public void Build_ShouldBuildLinkForChatKind_WhenCalled(
        long externalChatId,
        int externalMessageId,
        string? chatUsername,
        string? expectedUrl)
    {
        Assert.Equal(expectedUrl, TelegramMessageUrlBuilder.Build(externalChatId, externalMessageId, chatUsername));
    }
}
