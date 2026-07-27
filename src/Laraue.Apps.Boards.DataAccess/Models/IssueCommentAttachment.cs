namespace Laraue.Apps.Boards.DataAccess.Models;

public class IssueCommentAttachment
{
    public Guid AttachmentId { get; set; }
    public Attachment? Attachment { get; set; }
    
    public long CommentId { get; set; }
    public IssueComment? Comment { get; set; }
}