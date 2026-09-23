using Laraue.Apps.Boards.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[ApiController]
[Route("/api/files")]
public class FilesController(
    ICoreFilesService coreFilesService,
    IFileStorage fileStorage,
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache)
    : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetFileById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var location = await coreFilesService.ResolveFileLocation(id, cancellationToken);

        // Serve from local cache — seekable stream, ASP.NET Core handles ranges
        if (await fileStorage.FileExists(location.PhysicalPath, cancellationToken))
        {
            var cachedStream = await fileStorage.ReadFile(location.PhysicalPath, cancellationToken);
            return File(cachedStream, location.MimeType, enableRangeProcessing: true);
        }

        // Resolve and cache the Telegram download URL (valid 60 min) — this caching is
        // controller-specific, so it stays here rather than in
        // ICoreFilesService.ResolveTelegramDownloadUrl, which always asks Telegram fresh.
        var cacheKey = $"tg_file_url_{location.ExternalFileId}";
        if (!memoryCache.TryGetValue(cacheKey, out string? downloadUrl))
        {
            downloadUrl = await coreFilesService.ResolveTelegramDownloadUrl(location.ExternalFileId, cancellationToken);
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
        return File(stream, location.MimeType, enableRangeProcessing: false);
    }
}