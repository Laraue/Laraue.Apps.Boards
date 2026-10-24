using System.Collections.Concurrent;
using System.Net;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for Telegram's file-download URL in the Telegram test host, serving each file's content by its file
/// id - e.g. a document sent to the bot, whose type is checked by its content. The path GetFile reports is
/// "{fileId}/path" (<see cref="TelegramBotClientMockFactory"/>), so the URL ends with it. A file not in
/// <paramref name="fileContents"/> is a 404. Every requested file id is added to <paramref name="downloadedFileIds"/>.
/// </summary>
public class FakeTelegramFileContentHttpMessageHandler(
    IReadOnlyDictionary<string, byte[]> fileContents,
    ConcurrentQueue<string> downloadedFileIds)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var filePath = path[..^TelegramBotClientMockFactory.FilePathSuffix.Length];
        var fileId = filePath[(filePath.LastIndexOf('/') + 1)..];
        downloadedFileIds.Enqueue(fileId);

        var response = fileContents.TryGetValue(fileId, out var content)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);

        return Task.FromResult(response);
    }
}
