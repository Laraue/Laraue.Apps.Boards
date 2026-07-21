using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.Services;

public class MediaInfo
{
    public Guid? PreviewFileId { get; set; }
    public Guid OriginalFileId { get; set; }
    public AttachmentType Type { get; set; }
}