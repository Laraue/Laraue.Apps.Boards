using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Ai;
using Laraue.Apps.Boards.Services.Billing;
using Laraue.Apps.Boards.TelegramServices.Resources;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

/// <summary>
/// What to tell the user when a save is refused: the one list of save errors and their texts, for auto-save,
/// /save and the "where should this go?" picker alike.
/// </summary>
public static class SaveRefusal
{
    /// <returns>The text for an expected save refusal; null for any other exception, which should propagate.</returns>
    public static string? GetMessage(Exception exception) => exception switch
    {
        ChatNotLinkedException => Phrases.LinkNotLinked,
        LinkTargetDeletedException => Phrases.LinkTargetDeleted,
        IssueCreationForbiddenException => Phrases.IssueCreationForbidden,
        IssueLimitExceededException => Phrases.IssueLimitExceeded,

        // E.g. an edited message whose card is with the agent: the card keeps the text that was handed off.
        IssueLockedByAgentRunException => Phrases.IssueLockedByAgentRun,
        AiContentSummarizationException => Phrases.AiSummarizationUnavailable,
        InsufficientTokenBalanceException => Phrases.InsufficientTokenBalance,
        _ => null,
    };
}
