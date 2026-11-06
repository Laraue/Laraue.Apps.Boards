using Google.Protobuf;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Storage.Internal.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BoardsFile = Laraue.Apps.Boards.DataAccess.Models.File;
using StorageService = Laraue.Apps.Storage.Internal.Contracts.StorageService;

namespace Laraue.Apps.Boards.Services.Storage;

public interface IStorageFilesService
{
    /// <summary>
    /// Uploads an attachment to Laraue.Apps.Storage for <paramref name="organizationId"/> and saves its
    /// <see cref="BoardsFile"/> and <see cref="StorageFile"/> rows, and the preview's when Storage made one.
    /// <paramref name="fileType"/> is what <see cref="AttachmentFileTypes.Detect"/> recognized - the caller validates
    /// first. The upload's reference stays pending until <see cref="Confirm"/>: if the caller never gets there,
    /// Storage deletes the upload after a day. Counts toward the organization's storage quota (BRD-340): throws
    /// <see cref="StorageQuotaExceededException"/>, with nothing stored, when the file doesn't fit.
    /// </summary>
    Task<MediaInfo> Upload(
        long organizationId,
        string fileName,
        AttachmentFileType fileType,
        byte[] content,
        CancellationToken cancellationToken);

    /// <summary>
    /// The public URL of a file kept in Storage, or null when the file isn't there (an older upload or a file from
    /// Telegram - those are served from Telegram, BRD-342).
    /// </summary>
    Task<string?> FindUrl(Guid fileId, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads a file kept in Storage from its public URL, or returns null when the file isn't there. The caller owns
    /// the stream.
    /// </summary>
    Task<FileContent?> OpenContent(Guid fileId, CancellationToken cancellationToken);
}

public class StorageFilesService(
    StorageService.StorageServiceClient storageClient,
    IStorageFileUrlBuilder urlBuilder,
    IStorageQuotaService quotaService,
    IHttpClientFactory httpClientFactory,
    DatabaseContext context,
    ILogger<StorageFilesService> logger)
    : IStorageFilesService
{
    /// <summary>
    /// The named <see cref="HttpClient"/> that downloads files from Storage's public host.
    /// </summary>
    public const string HttpClientName = "storage-files";

    public async Task<MediaInfo> Upload(
        long organizationId,
        string fileName,
        AttachmentFileType fileType,
        byte[] content,
        CancellationToken cancellationToken)
    {
        fileName = AttachmentFileTypes.SanitizeFileName(fileName, fileType);

        // Storage checks it under a lock on the owner, so two uploads at once can't both pass.
        var quotaBytes = await quotaService.GetQuotaBytes(organizationId, cancellationToken);

        var response = await storageClient.UploadFileAsync(
            new UploadFileRequest
            {
                Owner = new Owner { OrganizationId = organizationId },
                FileName = fileName,
                ContentType = fileType.MimeType,
                Content = ByteString.CopyFrom(content),
                QuotaBytes = quotaBytes,
            },
            cancellationToken: cancellationToken);

        if (response.QuotaExceeded is { } exceeded)
        {
            logger.LogInformation(
                "Upload of {FileName} to organization {OrganizationId} refused: {UsageBytes} of {QuotaBytes} bytes used, {RequiredBytes} needed",
                fileName,
                organizationId,
                exceeded.OwnerUsageBytes,
                exceeded.QuotaBytes,
                exceeded.RequiredBytes);

            throw new StorageQuotaExceededException(
                exceeded.OwnerUsageBytes,
                exceeded.QuotaBytes,
                exceeded.RequiredBytes);
        }

        var uploaded = response.Uploaded
            ?? throw new InvalidOperationException($"Storage didn't store {fileName}: {response.ResultCase}.");

        var original = ToFile(uploaded.File, Guid.Parse(uploaded.ReferenceId));
        context.Files.Add(original);

        BoardsFile? preview = null;
        if (uploaded.Preview is not null)
        {
            preview = ToFile(uploaded.Preview, referenceId: null);
            context.Files.Add(preview);
        }

        await context.SaveChangesAsync(cancellationToken);

        return new MediaInfo
        {
            OriginalFileId = original.Id,
            PreviewFileId = preview?.Id,
            Type = fileType.AttachmentType,
            FileName = fileName,
            StorageReferenceId = original.StorageFile!.ReferenceId,
        };
    }

    public async Task<string?> FindUrl(Guid fileId, CancellationToken cancellationToken)
    {
        var storageFileId = await context.StorageFiles
            .Where(x => x.FileId == fileId)
            .Select(x => (Guid?)x.ExternalFileId)
            .FirstOrDefaultAsync(cancellationToken);

        return storageFileId is null ? null : urlBuilder.Build(storageFileId.Value);
    }

    public async Task<FileContent?> OpenContent(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await context.StorageFiles
            .Where(x => x.FileId == fileId)
            .Select(x => new { x.ExternalFileId, x.File!.MimeType })
            .FirstOrDefaultAsync(cancellationToken);

        if (file is null)
            return null;

        var stream = await httpClientFactory
            .CreateClient(HttpClientName)
            .GetStreamAsync(urlBuilder.Build(file.ExternalFileId), cancellationToken);

        return new FileContent(stream, file.MimeType ?? "application/octet-stream");
    }

    private static BoardsFile ToFile(StoredFile storedFile, Guid? referenceId)
    {
        return new BoardsFile
        {
            Name = storedFile.FileName,
            Size = storedFile.Size,
            MimeType = storedFile.ContentType,
            StorageFile = new StorageFile
            {
                ExternalFileId = Guid.Parse(storedFile.Id),
                ReferenceId = referenceId,
            },
        };
    }
}
