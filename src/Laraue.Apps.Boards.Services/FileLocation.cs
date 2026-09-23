namespace Laraue.Apps.Boards.Services;

/// <summary>
/// A previously uploaded file's resolved local-cache path, content type, and Telegram file id -
/// the lookup every caller that needs to locate a file shares, whether serving it directly
/// (<c>WebApiHost.FilesController.GetFileById</c>) or reading its full content
/// (<see cref="ICoreFilesService.GetFileContent"/>), instead of each re-deriving it independently
/// from <see cref="DataAccess.Models.File"/>/<see cref="DataAccess.Models.TelegramFile"/>.
/// </summary>
public record FileLocation(string PhysicalPath, string MimeType, string ExternalFileId);
