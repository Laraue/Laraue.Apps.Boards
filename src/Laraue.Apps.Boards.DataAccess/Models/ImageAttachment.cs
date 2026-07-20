namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// The attachment that was uploaded from WEB interface.
/// </summary>
public class ImageAttachment
{
    public long Id { get; set; }
    public Guid AttachmentId { get; set; }
    public Attachment? Attachment { get; set; }
    
    public Guid ThumbnailTelegramFileId { get; set; }
    public TelegramFile? ThumbnailTelegramFile { get; set; }
    
    public Guid OriginalTelegramFileId { get; set; }
    public TelegramFile? OriginalTelegramFile { get; set; }
}