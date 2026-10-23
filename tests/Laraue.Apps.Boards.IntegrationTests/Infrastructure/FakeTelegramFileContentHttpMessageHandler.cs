using System.Net;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for Telegram's file-download URL in the Telegram test host, serving each file's content by its file
/// id - e.g. a document sent to the bot, whose type is checked by its content. The path GetFile reports is
/// "{fileId}/path" (<see cref="TelegramBotClientMockFactory"/>), so the URL ends with it. A file not in
/// <paramref name="fileContents"/> is a 404.
/// </summary>
public class FakeTelegramFileContentHttpMessageHandler(IReadOnlyDictionary<string, byte[]> fileContents)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var filePath = path[..^TelegramBotClientMockFactory.FilePathSuffix.Length];
        var fileId = filePath[(filePath.LastIndexOf('/') + 1)..];

        var response = fileContents.TryGetValue(fileId, out var content)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);

        return Task.FromResult(response);
    }
}
