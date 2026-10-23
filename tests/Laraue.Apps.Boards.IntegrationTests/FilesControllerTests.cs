using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class FilesControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task GetFileById_ShouldServeDocumentAsDownload_WhenFileIsDocument()
    {
        var fileId = await UploadAsync("spec.pdf", AttachmentFileTypes.Pdf, "%PDF-1.7\n"u8.ToArray());

        var response = await host.CreateClient().GetAsync($"/api/files/{fileId}");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("spec.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.StartsWith("sandbox", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task GetFileById_ShouldServeImageInline_WhenFileIsImage()
    {
        var fileId = await UploadAsync("photo.png", AttachmentFileTypes.Png, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var response = await host.CreateClient().GetAsync($"/api/files/{fileId}");

        response.EnsureSuccessStatusCode();
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    private async Task<Guid> UploadAsync(string fileName, AttachmentFileType fileType, byte[] content)
    {
        using var testScope = host.CreateTestScope();
        var mediaInfo = await testScope.Services.GetRequiredService<ICoreFilesService>().UploadFile(
            fileName,
            fileType,
            new MemoryStream(content),
            CancellationToken.None);

        return mediaInfo.OriginalFileId;
    }
}
