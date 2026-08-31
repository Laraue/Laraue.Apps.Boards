using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices;

namespace Laraue.Apps.Boards.IntegrationTests;

public class IssuePreviewFormatterTests
{
    [Fact]
    public void BuildContentGenerationErrorText_ShouldProduceFullyEscapedMarkdownV2Text()
    {
        // Sent with ParseMode.MarkdownV2 (see IssuePreviewReplySender/SearchService) - if this
        // fallback text itself weren't properly escaped, a bug in formatting one issue's content
        // would just be replaced by a *second*, harder-to-diagnose Telegram API rejection instead
        // of the safe placeholder it's supposed to be.
        var key = new IssueKey("BRD", 42);

        var result = IssuePreviewFormatter.BuildContentGenerationErrorText(key, "Laraue Corp");

        Assert.Equal(
            "📋 *BRD\\-42* · Laraue Corp\n" +
            "⚠️ Something went wrong while generating the content of this message\\.",
            result);
    }
}
