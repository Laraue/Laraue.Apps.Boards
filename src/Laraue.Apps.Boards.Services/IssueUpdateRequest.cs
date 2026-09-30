namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Describes a partial update to an existing issue, built fluently and passed to
/// <see cref="ICoreIssuesService.Update"/>. Only the properties that were actually set are
/// touched (and logged to history); this is what lets both the Web API (which always sends
/// content/assignee together) and Telegram (which only ever touches content or an attachment on
/// its own) go through the same method instead of one method per case.
/// </summary>
public class IssueUpdateRequest : IssueChange<IssueUpdateRequest>
{
    /// <summary>
    /// A title given by a client (the web app, an MCP client) - wins over any suggested title.
    /// </summary>
    internal ChangedValue<string> Title { get; private set; } = ChangedValue<string>.Unset;

    /// <summary>
    /// A title produced by the system (Telegram's first line or AI summary). Applied only while the
    /// title wasn't set by hand.
    /// </summary>
    internal string? SuggestedTitle { get; private set; }

    /// <summary>
    /// Sets the title the client gave. A null/blank <paramref name="title"/> is ignored - the callers
    /// that must have a title (the web app, MCP) validate it before getting here.
    /// </summary>
    public IssueUpdateRequest SetTitle(string? title)
    {
        if (IssueTitle.Normalize(title) is { Length: > 0 } normalized)
            Title = ChangedValue<string>.Of(normalized);

        return this;
    }

    /// <summary>
    /// Suggests a title for a changed content. Ignored when the title was set by hand.
    /// </summary>
    public IssueUpdateRequest SetSuggestedTitle(string? title)
    {
        SuggestedTitle = IssueTitle.Normalize(title) is { Length: > 0 } normalized ? normalized : null;
        return this;
    }

    internal List<Guid> AttachmentIdsToUnlink { get; } = [];

    public IssueUpdateRequest UnlinkAttachment(Guid attachmentId)
    {
        AttachmentIdsToUnlink.Add(attachmentId);
        return this;
    }

    public IssueUpdateRequest UnlinkAttachments(IEnumerable<Guid> attachmentIds)
    {
        AttachmentIdsToUnlink.AddRange(attachmentIds);
        return this;
    }
}
