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
    /// Upload file and returns it internal identifiers.
    /// </summary>
    Task<MediaInfo> UploadFile(
        string fileName,
        string contentType,
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
        string contentType,
        Stream stream,
        CancellationToken cancellationToken)
    {
        if (SystemMimeTypes.Images.Contains(contentType))
            return UploadPhotoFile(fileName, contentType, stream, cancellationToken);
        
        throw new InvalidOperationException( $"Content type uploading {contentType} is not supported");
    }

    public async Task<FileContent> GetFileContent(Guid fileId, CancellationToken cancellationToken)
    {
        var fileData = await context.Files
            .Where(x => x.Id == fileId)
            .Select(x => new
            {
                x.TelegramFile!.ExternalFileUniqueId,
                x.MimeType,
                x.TelegramFile.ExternalFileId,
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"File: {fileId} is not found");

        var mimeType = fileData.MimeType ?? "application/octet-stream";
        var extension = ExtensionUtility.GetExtension(fileData.MimeType);
        var physicalPath = ShardedPathStrategy.GetPath(fileData.ExternalFileUniqueId, extension);

        if (await fileStorage.FileExists(physicalPath, cancellationToken))
        {
            var cachedStream = await fileStorage.ReadFile(physicalPath, cancellationToken);
            return new FileContent(cachedStream, mimeType);
        }

        var tgFile = await botClient.GetFile(fileData.ExternalFileId, cancellationToken);
        if (string.IsNullOrEmpty(tgFile.FilePath))
            throw new NotFoundException($"File: {fileId} is not available for download");

        var downloadUrl = $"https://api.telegram.org/file/bot{options.Value.Token}/{tgFile.FilePath}";
        var httpClient = httpClientFactory.CreateClient();
        var contentStream = await httpClient.GetStreamAsync(downloadUrl, cancellationToken);

        return new FileContent(contentStream, mimeType);
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
