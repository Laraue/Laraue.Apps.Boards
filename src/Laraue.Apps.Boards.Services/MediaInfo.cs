using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.Services;

public record MediaInfo
{
    public required Guid? PreviewFileId { get; set; }
    public required Guid OriginalFileId { get; set; }
    public required AttachmentType Type { get; set; }
    public required string? FileName { get; set; }
}