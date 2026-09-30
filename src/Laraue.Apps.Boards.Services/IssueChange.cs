using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.AttributeRequests;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Fluent surface shared by <see cref="IssueCreateRequest"/> and <see cref="IssueUpdateRequest"/>: content,
/// assignee, attributes and attachment linking. Only what's actually set gets applied (and
/// logged to history where relevant) - this is what lets <see cref="ICoreIssuesService.Create"/>
/// and <see cref="ICoreIssuesService.Update"/> share one shape instead of each caller juggling
/// its own bespoke set of positional parameters.
/// </summary>
public abstract class IssueChange<TSelf> where TSelf : IssueChange<TSelf>
{
    internal ChangedValue<string?> Content { get; private set; } = ChangedValue<string?>.Unset;

    internal ChangedValue<Guid> AssigneeId { get; private set; } = ChangedValue<Guid>.Unset;

    /// <summary>
    /// Unset means "don't touch attributes at all" (Telegram never sets this). Set to an empty
    /// list means "this issue should have no attributes" - clear whatever's there.
    /// </summary>
    internal ChangedValue<IReadOnlyList<SetIssueAttributeRequest>> Attributes { get; private set; } =
        ChangedValue<IReadOnlyList<SetIssueAttributeRequest>>.Unset;

    internal List<MediaInfo> NewAttachments { get; } = [];

    internal List<Guid> AttachmentIdsToLink { get; } = [];

    /// <summary>
    /// Normalizes line endings to <see cref="IssueContentFormat.LineSeparator"/> before storing -
    /// the web app and Telegram send the same text with different line endings (browser textarea
    /// vs. Telegram's message text), which otherwise makes
    /// <see cref="ICoreIssuesService.Update"/>'s content-equality check see a "change" and log a
    /// history entry whose old/new values render identically.
    /// </summary>
    public TSelf SetContent(string? content)
    {
        Content = ChangedValue<string?>.Of(content?.ReplaceLineEndings(IssueContentFormat.LineSeparatorString));
        return (TSelf)this;
    }

    public TSelf SetAssignee(Guid assigneeId)
    {
        AssigneeId = ChangedValue<Guid>.Of(assigneeId);
        return (TSelf)this;
    }

    public TSelf SetAttributes(IEnumerable<SetIssueAttributeRequest> attributes)
    {
        Attributes = ChangedValue<IReadOnlyList<SetIssueAttributeRequest>>.Of(attributes.ToList());
        return (TSelf)this;
    }

    /// <summary>
    /// Uploads and links a brand-new attachment (e.g. a file attached to a Web API request).
    /// </summary>
    public TSelf LinkNewAttachment(MediaInfo mediaInfo)
    {
        NewAttachments.Add(mediaInfo);
        return (TSelf)this;
    }

    public TSelf LinkNewAttachments(IEnumerable<MediaInfo> mediaInfos)
    {
        NewAttachments.AddRange(mediaInfos);
        return (TSelf)this;
    }

    /// <summary>
    /// Links an already-persisted <see cref="Attachment"/> (e.g. one Telegram already stored for
    /// a message before the message had a card). No-op for any id already linked to any issue.
    /// </summary>
    public TSelf LinkExistingAttachment(Guid attachmentId)
    {
        AttachmentIdsToLink.Add(attachmentId);
        return (TSelf)this;
    }

    public TSelf LinkExistingAttachments(IEnumerable<Guid> attachmentIds)
    {
        AttachmentIdsToLink.AddRange(attachmentIds);
        return (TSelf)this;
    }
}
