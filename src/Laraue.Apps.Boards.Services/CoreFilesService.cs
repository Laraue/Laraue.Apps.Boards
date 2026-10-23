using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.Services;

public interface ICoreFilesService
{
    /// <summary>
    /// Download file from telegram to local storage.
    /// </summary>
    Task DownloadToLocalStorage(string fileId, string? mimeType, CancellationToken cancellationToken);

    /// <summary>
    /// Upload file and returns it internal identifiers. <paramref name="fileType"/> is the type
    /// <see cref="AttachmentFileTypes.Detect"/> recognized - the caller validates before uploading.
    /// </summary>
    Task<MediaInfo> UploadFile(
        string fileName,
        AttachmentFileType fileType,
        Stream stream,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens a stream over a previously uploaded file's content - from local storage if already
    /// cached there, else a live download from Telegram (the same two-path lookup
    /// <c>WebApiHost.FilesController.GetFileById</c> uses, minus its HTTP Range/streaming
    /// support, which only makes sense for a real HTTP response, not a single tool-call result).
    /// The caller owns the returned stream and must dispose it.
    /// </summary>
    Task<FileContent> GetFileContent(Guid fileId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a previously uploaded file's local-cache path, mime type, and Telegram file id -
    /// the lookup shared by every caller that needs to locate a file, whether serving it directly
    /// (<c>FilesController.GetFileById</c>) or reading its content (<see cref="GetFileContent"/>).
    /// </summary>
    Task<FileLocation> ResolveFileLocation(Guid fileId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a live, downloadable Telegram URL for a file not present in the local cache.
    /// Callers that need one repeatedly within a short window (e.g. serving Range requests)
    /// should cache the result themselves - this always asks Telegram for a fresh one.
    /// </summary>
    Task<string> ResolveTelegramDownloadUrl(string externalFileId, CancellationToken cancellationToken);
}

public class CoreFilesService(
    IFileStorage fileStorage,
    ITelegramBotClient botClient,
    IOptions<TelegramOptions> options,
    IHttpClientFactory httpClientFactory,
    DatabaseContext context)
    : ICoreFilesService
{

    public async Task DownloadToLocalStorage(string fileId, string? mimeType, CancellationToken cancellationToken)
    {
        var botFile = await botClient.GetFile(fileId, cancellationToken);

        var stream = new MemoryStream();
        await botClient.DownloadFile(
            botFile,
            stream,
            cancellationToken);
        
        var extension = ExtensionUtility.GetExtension(mimeType);
        var filePath = ShardedPathStrategy.GetPath(
            botFile.FileUniqueId,
            extension);
        
        stream.Position = 0;
        await fileStorage.WriteFile(
            filePath,
            stream,
            null,
            cancellationToken);
    }

    public Task<MediaInfo> UploadFile(
        string fileName,
        AttachmentFileType fileType,
        Stream stream,
        CancellationToken cancellationToken)
    {
        fileName = AttachmentFileTypes.SanitizeFileName(fileName, fileType);

        // Telegram makes a thumbnail only for what it takes as a photo (JPEG, PNG); the rest is kept
        // as a document, byte for byte.
        if (fileType == AttachmentFileTypes.Jpeg || fileType == AttachmentFileTypes.Png)
            return UploadPhotoFile(fileName, fileType.MimeType, stream, cancellationToken);

        return UploadDocumentFile(fileName, fileType, stream, cancellationToken);
    }

    public async Task<FileContent> GetFileContent(Guid fileId, CancellationToken cancellationToken)
    {
        var location = await ResolveFileLocation(fileId, cancellationToken);

        if (await fileStorage.FileExists(location.PhysicalPath, cancellationToken))
        {
            var cachedStream = await fileStorage.ReadFile(location.PhysicalPath, cancellationToken);
            return new FileContent(cachedStream, location.MimeType);
        }

        var downloadUrl = await ResolveTelegramDownloadUrl(location.ExternalFileId, cancellationToken);
        var httpClient = httpClientFactory.CreateClient();
        var contentStream = await httpClient.GetStreamAsync(downloadUrl, cancellationToken);

        return new FileContent(contentStream, location.MimeType);
    }

    public async Task<FileLocation> ResolveFileLocation(Guid fileId, CancellationToken cancellationToken)
    {
        var fileData = await context.Files
            .Where(x => x.Id == fileId)
            .Select(x => new
            {
                x.TelegramFile!.ExternalFileUniqueId,
                x.MimeType,
                x.TelegramFile.ExternalFileId,
                x.Name,
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"File: {fileId} is not found");

        var mimeType = fileData.MimeType ?? "application/octet-stream";
        var extension = ExtensionUtility.GetExtension(fileData.MimeType);
        var physicalPath = ShardedPathStrategy.GetPath(fileData.ExternalFileUniqueId, extension);

        return new FileLocation(physicalPath, mimeType, fileData.ExternalFileId, fileData.Name);
    }

    public async Task<string> ResolveTelegramDownloadUrl(string externalFileId, CancellationToken cancellationToken)
    {
        var tgFile = await botClient.GetFile(externalFileId, cancellationToken);
        if (string.IsNullOrEmpty(tgFile.FilePath))
            throw new NotFoundException("Telegram file path is unavailable.");

        return $"https://api.telegram.org/file/bot{options.Value.Token}/{tgFile.FilePath}";
    }

    private async Task<MediaInfo> UploadPhotoFile(
        string fileName,
        string contentType,
        Stream stream,
        CancellationToken cancellationToken)
    {
        // Upload file to telegram
        var message = await botClient.SendPhoto(
            options.Value.FilesChatId,
            new InputFileStream(stream, fileName),
            cancellationToken: cancellationToken);

        var thumbnailPhoto = message.Photo![0];
        var thumbnail = ToFile(thumbnailPhoto, fileName, contentType);
        var originalPhoto = message.Photo![^1];
        var original = ToFile(originalPhoto, fileName, contentType);
        
        // store preview to the local storage
        await DownloadToLocalStorage(thumbnail.FileId, contentType, cancellationToken);
        
        // store file identifiers to DB
        var thumbnailFileId = await UpsertDbFile(thumbnail, cancellationToken);
        var originalFileId = await UpsertDbFile(original, cancellationToken);

        return new MediaInfo
        {
            OriginalFileId = originalFileId,
            PreviewFileId = thumbnailFileId,
            Type = AttachmentType.Image,
            FileName = fileName,
        };
    }

    private async Task<MediaInfo> UploadDocumentFile(
        string fileName,
        AttachmentFileType fileType,
        Stream stream,
        CancellationToken cancellationToken)
    {
        // Without disableContentTypeDetection Telegram may turn a GIF into an animation or a WebP
        // into a sticker; with it the message carries the document as uploaded.
        var message = await botClient.SendDocument(
            options.Value.FilesChatId,
            new InputFileStream(stream, fileName),
            disableContentTypeDetection: true,
            cancellationToken: cancellationToken);

        var document = message.Document!;
        var originalFileId = await UpsertDbFile(
            new File
            {
                FileUniqueId = document.FileUniqueId,
                FileName = fileName,
                FileId = document.FileId,
                FileSize = document.FileSize,
                MimeType = fileType.MimeType,
            },
            cancellationToken);

        // A GIF or WebP is small enough (MaxImageSizeBytes) to be its own preview.
        var isImage = fileType.AttachmentType == AttachmentType.Image;

        return new MediaInfo
        {
            OriginalFileId = originalFileId,
            PreviewFileId = isImage ? originalFileId : null,
            Type = fileType.AttachmentType,
            FileName = fileName,
        };
    }

    private async Task<Guid> UpsertDbFile(
        File file,
        CancellationToken cancellationToken)
    {
        var oldFileData = await context.TelegramFiles
            .Where(x => x.ExternalFileUniqueId == file.FileUniqueId)
            .Select(x => new { x.FileId })
            .FirstOrDefaultAsync(cancellationToken);

        if (oldFileData is not null)
            return oldFileData.FileId;
        
        var telegramFile = new TelegramFile
        {
            ExternalFileId = file.FileId,
            ExternalFileUniqueId = file.FileUniqueId,
            File = new DataAccess.Models.File
            {
                Name = file.FileName,
                Size = file.FileSize,
                MimeType = file.MimeType,
            }
        };
            
        context.Add(telegramFile);
        await context.SaveChangesAsync(cancellationToken);

        return telegramFile.FileId; // TODO - check is that work actually
    }

    private static File ToFile(PhotoSize photoSize, string fileName, string contentType)
    {
        return new File
        {
            FileUniqueId = photoSize.FileUniqueId,
            FileName = fileName,
            FileId = photoSize.FileId,
            FileSize = photoSize.FileSize,
            MimeType = contentType,
        };
    }
}

public class PhotoFile : File
{
    public required int Width { get; set; }
    public required int Height { get; set; }
}

public class File
{
    public required long? FileSize { get; set; }
    public required string FileId { get; set; }
    public required string FileUniqueId { get; set; }
    public required string? FileName { get; set; }
    public required string? MimeType { get; set; }
}
