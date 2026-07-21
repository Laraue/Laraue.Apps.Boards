using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// Represents one attachment in the system. Attachment is the set
/// of files represents one attachment to issue, comment etc.
/// </summary>
public class Attachment
{
    /// <summary>
    /// Unique system attachment identifier.
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// When the attachment was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Who created the attachment.
    /// </summary>
    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }
    
    /// <summary>
    /// Attachment preview. Used for images and video attachments.
    /// </summary>
    public Guid? PreviewFileId { get; set; }
    public File? PreviewFile { get; set; }
    
    /// <summary>
    /// Attachment file.
    /// </summary>
    public Guid FileId { get; set; }
    public File? File { get; set; }
    
    /// <summary>
    /// Attachment type. Photo / Video / File should be handled differently on frontend. 
    /// </summary>
    public AttachmentType Type { get; set; }
    
    public IssueAttachment? IssueAttachment { get; set; }
}

public enum AttachmentType
{
    /// <summary>
    /// The image attachment consists of preview and original file.
    /// </summary>
    Image,
    
    /// <summary>
    /// The video attachment consists of preview image and original video file.
    /// </summary>
    Video,
}