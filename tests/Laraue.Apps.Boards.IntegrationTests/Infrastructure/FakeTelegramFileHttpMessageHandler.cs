using System.Net;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for a real network call to Telegram's file-download URL
/// (<c>CoreFilesService.GetFileContent</c>'s fallback path, taken whenever a file isn't already
/// in the local cache - e.g. an issue attachment's original file, which
/// <c>CoreFilesService.UploadPhotoFile</c> only ever caches the preview/thumbnail for, not the
/// original). Returns fixed bytes for any request rather than actually reaching Telegram.
/// </summary>
public class FakeTelegramFileHttpMessageHandler : HttpMessageHandler
{
    public static readonly byte[] Content = [1, 2, 3, 4, 5];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Content),
        };

        return Task.FromResult(response);
    }
}
