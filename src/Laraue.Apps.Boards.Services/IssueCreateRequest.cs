namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Describes a new issue, built fluently and passed to <see cref="ICoreIssuesService.Create"/>.
/// Status, creation time and title are mandatory - an issue can't exist without them. Ownership is not
/// part of this object - like <see cref="ICoreIssuesService.Update"/> takes its updater id
/// separately from <see cref="IssueUpdateRequest"/>, <see cref="ICoreIssuesService.Create"/> takes the
/// owner id separately from this one. Everything else is optional via the shared
/// <see cref="IssueChange{TSelf}"/> setters.
/// </summary>
public class IssueCreateRequest(long statusId, DateTime createdAt, string title, bool isTitleSetExplicitly)
    : IssueChange<IssueCreateRequest>
{
    internal long StatusId { get; } = statusId;

    /// <summary>
    /// The issue title - required. A client that gives it (the web app, MCP) passes
    /// <c>isTitleSetExplicitly: true</c>; Telegram derives it from the message and passes false, so a
    /// later edit of the message can derive it again.
    /// </summary>
    internal string Title { get; } = IssueTitle.Normalize(title) is { Length: > 0 } normalized
        ? normalized
        : throw new ArgumentException("An issue needs a title.", nameof(title));

    internal bool IsTitleSetExplicitly { get; } = isTitleSetExplicitly;
    internal DateTime CreatedAt { get; } = createdAt;
    internal long? TelegramMessageId { get; private set; }
    internal bool IsSample { get; private set; }

    public IssueCreateRequest SetTelegramMessageId(long telegramMessageId)
    {
        TelegramMessageId = telegramMessageId;
        return this;
    }

    /// <summary>
    /// Marks the issue as one Boards adds itself - the setup window's sample project (BRD-293), which the user
    /// didn't write. It isn't counted toward the plan's monthly issue limit or in <c>boards_issues_created_total</c>.
    /// </summary>
    public IssueCreateRequest MarkAsSample()
    {
        IsSample = true;
        return this;
    }
}
