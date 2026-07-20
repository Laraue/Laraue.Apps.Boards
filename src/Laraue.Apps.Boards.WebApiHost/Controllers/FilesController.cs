using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[ApiController]
[Route("/api/files")]
public class FilesController(
    DatabaseContext db,
    IFileStorage fileStorage,
    ITelegramBotClient botClient,
    IHttpClientFactory httpClientFactory,
    IOptions<TelegramOptions> options,
    IMemoryCache memoryCache)
    : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetFileById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var fileData = await db.TelegramFiles
            .Where(x => x.Id == id)
            .Select(x => new { x.FileUniqueId, x.MimeType, x.FileId })
            .FirstOrThrowNotFoundEFAsync("File is not found", cancellationToken);

        var fileExtension = ExtensionUtility.GetExtension(fileData.MimeType);
        var physicalPath = ShardedPathStrategy.GetPath(fileData.FileUniqueId, fileExtension);
        var mimeType = fileData.MimeType ?? "application/octet-stream";

        // Serve from local cache — seekable stream, ASP.NET Core handles ranges
        if (await fileStorage.FileExists(physicalPath, cancellationToken))
        {
            var cachedStream = await fileStorage.ReadFile(physicalPath, cancellationToken);
            return File(cachedStream, mimeType, enableRangeProcessing: true);
        }

        // Resolve and cache the Telegram download URL (valid 60 min)
        var botToken = options.Value.Token;
        var cacheKey = $"tg_file_url_{fileData.FileId}";
        if (!memoryCache.TryGetValue(cacheKey, out string? downloadUrl))
        {
            var tgFile = await botClient.GetFile(fileData.FileId, cancellationToken);
            if (string.IsNullOrEmpty(tgFile.FilePath))
                return NotFound("Telegram file path is unavailable.");

            downloadUrl = $"https://api.telegram.org/file/bot{botToken}/{tgFile.FilePath}";
            memoryCache.Set(cacheKey, downloadUrl, TimeSpan.FromMinutes(55));
        }

        // Forward the request to Telegram, proxying the Range header if present
        var httpClient = httpClientFactory.CreateClient();
        var rangeHeader = Request.Headers.Range.ToString();
        if (!string.IsNullOrEmpty(rangeHeader))
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Range", rangeHeader);

        var telegramResponse = await httpClient.GetAsync(
            downloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        telegramResponse.EnsureSuccessStatusCode();

        // Mirror status code first, before writing any headers
        Response.StatusCode = (int)telegramResponse.StatusCode;
        Response.Headers.Append("Accept-Ranges", "bytes");

        // Content-Length comes from content headers
        if (telegramResponse.Content.Headers.ContentLength is { } contentLength)
            Response.Headers.ContentLength = contentLength;

        // Content-Range is also a content header on 206 responses
        if (telegramResponse.Content.Headers.ContentRange is { } contentRange)
            Response.Headers.Append("Content-Range", contentRange.ToString());

        var stream = await telegramResponse.Content.ReadAsStreamAsync(cancellationToken);

        // enableRangeProcessing: false — we've already handled the range manually
        // by forwarding it to Telegram. ASP.NET Core must not try to slice again.
        return File(stream, mimeType, enableRangeProcessing: false);
    }
}