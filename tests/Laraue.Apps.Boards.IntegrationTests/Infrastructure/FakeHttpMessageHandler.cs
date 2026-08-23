namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

/// <summary>
/// Stubs out the transport layer for a typed <see cref="HttpClient"/> under test, so a unit test
/// can assert what was sent and control what comes back without any real network call.
/// </summary>
public class FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        return await respond(request);
    }
}
