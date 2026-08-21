namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Describes a new issue, built fluently and passed to <see cref="ICoreIssuesService.Create"/>.
/// Status and creation time are mandatory - an issue can't exist without them. Ownership is not
/// part of this object - like <see cref="ICoreIssuesService.Update"/> takes its updater id
/// separately from <see cref="IssueUpdateRequest"/>, <see cref="ICoreIssuesService.Create"/> takes the
/// owner id separately from this one. Everything else is optional via the shared
/// <see cref="IssueChange{TSelf}"/> setters.
/// </summary>
public class IssueCreateRequest(long statusId, DateTime createdAt) : IssueChange<IssueCreateRequest>
{
    internal long StatusId { get; } = statusId;
    internal DateTime CreatedAt { get; } = createdAt;
    internal long? TelegramMessageId { get; private set; }

    public IssueCreateRequest SetTelegramMessageId(long telegramMessageId)
    {
        TelegramMessageId = telegramMessageId;
        return this;
    }
}
