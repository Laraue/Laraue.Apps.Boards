using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.Services;

public interface ICoreFilesService
{
    /// <summary>
    /// Upload the file if it was not exists yet and return it system identifier.
    /// </summary>
    Task<Guid> CreateDbFileIfNotExists(
        File file,
        CancellationToken cancellationToken);
    
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
}

public class CoreFilesService(
    IFileStorage fileStorage,
    ITelegramBotClient botClient,
    IOptions<TelegramOptions> options,
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider)
    : ICoreFilesService
{
    public async Task<Guid> CreateDbFileIfNotExists(
        File file,
        CancellationToken cancellationToken)
    {
        var oldFileData = await context.TelegramFiles
            .Where(x => x.FileUniqueId == file.FileUniqueId)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (oldFileData is not null)
            return oldFileData.Id;
        
        var telegramFile = new TelegramFile
        {
            FileId = file.FileId,
            FileUniqueId = file.FileUniqueId,
            Name = file.FileName,
            Size = file.FileSize,
            MimeType = file.MimeType,
        };
            
        context.Add(telegramFile);
        await context.SaveChangesAsync(cancellationToken);

        return telegramFile.Id;
    }

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
        var uploadTask = contentType switch
        {
            "image/jpeg" or "image/png" => UploadPhotoFile(fileName, contentType, stream, cancellationToken),
            _ => throw new BadRequestException("file", $"Content type uploading {contentType} is not supported yet.")
        };
        
        return uploadTask;
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
        var thumbnailFileId = await CreateDbFileIfNotExists(thumbnail, cancellationToken);
        var originalFileId = await CreateDbFileIfNotExists(original, cancellationToken);

        return new MediaInfo
        {
            OriginalFileId = originalFileId,
            PreviewFileId = thumbnailFileId,
            Type = MediaType.Photo,
        };
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
