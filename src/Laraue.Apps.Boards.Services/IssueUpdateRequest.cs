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
