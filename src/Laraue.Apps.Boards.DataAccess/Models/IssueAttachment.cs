namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// Represents relation between attachment and issue.
/// </summary>
public class IssueAttachment
{
    public Guid AttachmentId { get; set; }
    public Attachment? Attachment { get; set; }
    
    public long IssueId { get; set; }
    public Issue? Issue { get; set; }
}