using System.Net;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using File = Laraue.Apps.Boards.DataAccess.Models.File;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class FilesControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task GetFileById_ShouldRedirectToStorage_WhenFileIsInStorage()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var upload = await testScope.Services.GetRequiredService<IStorageFilesService>().Upload(
            organization.Id,
            "spec.pdf",
            AttachmentFileTypes.Pdf,
            "%PDF-1.7\n"u8.ToArray(),
            CancellationToken.None);

        var response = await host
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync($"/api/files/{upload.OriginalFileId}");

        var storageFileId = host.StorageClient.Files.Values.Single().Id;
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"http://localhost:5565/{storageFileId}", response.Headers.Location?.ToString());
        Assert.Equal(TimeSpan.FromDays(1), response.Headers.CacheControl?.MaxAge);
    }

    [Fact]
    public async Task GetFileById_ShouldServeDocumentFromTelegram_WhenFileIsAnOlderUpload()
    {
        var fileId = await AddTelegramFileAsync("spec.pdf", "application/pdf");

        var response = await host.CreateClient().GetAsync($"/api/files/{fileId}");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("spec.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.StartsWith("sandbox", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal(FakeTelegramFileHttpMessageHandler.Content, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task GetFileById_ShouldServeImageInlineFromTelegram_WhenFileIsAnOlderUpload()
    {
        var fileId = await AddTelegramFileAsync("photo.png", "image/png");

        var response = await host.CreateClient().GetAsync($"/api/files/{fileId}");

        response.EnsureSuccessStatusCode();
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    /// <summary>
    /// A file uploaded before BRD-339: kept in the Telegram files chat, known only by its Telegram file id (BRD-342).
    /// </summary>
    private async Task<Guid> AddTelegramFileAsync(string fileName, string mimeType)
    {
        using var testScope = host.CreateTestScope();
        var telegramFile = new TelegramFile
        {
            ExternalFileId = $"old-upload-{Guid.NewGuid():N}",
            ExternalFileUniqueId = $"unique-{Guid.NewGuid():N}",
            File = new File
            {
                Name = fileName,
                MimeType = mimeType,
                Size = FakeTelegramFileHttpMessageHandler.Content.Length,
            },
        };
        testScope.Database.Add(telegramFile);
        await testScope.Database.SaveChangesAsync();

        return telegramFile.File.Id;
    }
}
