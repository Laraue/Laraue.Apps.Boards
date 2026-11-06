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
    /// Downloads a file from Telegram into memory, e.g. to check its type before keeping it. Returns null when
    /// it's larger than <paramref name="maxSizeBytes"/> - checked against the size Telegram reports before
    /// downloading and while reading.
    /// </summary>
    Task<byte[]?> DownloadFromTelegram(string fileId, int maxSizeBytes, CancellationToken cancellationToken);

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

    public async Task<byte[]?> DownloadFromTelegram(string fileId, int maxSizeBytes, CancellationToken cancellationToken)
    {
        var botFile = await botClient.GetFile(fileId, cancellationToken);
        if (botFile.FileSize > maxSizeBytes)
            return null;

        // The same download as GetFileContent's, capped while reading in case the reported size is wrong.
        var httpClient = httpClientFactory.CreateClient();
        await using var contentStream = await httpClient.GetStreamAsync(GetTelegramDownloadUrl(botFile), cancellationToken);

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await contentStream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxSizeBytes)
                return null;

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
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

        return GetTelegramDownloadUrl(tgFile);
    }

    private string GetTelegramDownloadUrl(TGFile tgFile)
    {
        if (string.IsNullOrEmpty(tgFile.FilePath))
            throw new NotFoundException("Telegram file path is unavailable.");

        return $"https://api.telegram.org/file/bot{options.Value.Token}/{tgFile.FilePath}";
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
