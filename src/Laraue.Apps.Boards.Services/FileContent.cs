namespace Laraue.Apps.Boards.Services;

/// <summary>
/// A file's content, as read by <see cref="ICoreFilesService.GetFileContent"/>. <see cref="Content"/>
/// is either a local file stream or a live Telegram download stream - the caller owns it and must
/// dispose it once done reading, and should only buffer it into memory at the point something
/// downstream (e.g. an MCP <c>ImageContentBlock</c>) actually requires a byte array.
/// </summary>
public record FileContent(Stream Content, string MimeType);
