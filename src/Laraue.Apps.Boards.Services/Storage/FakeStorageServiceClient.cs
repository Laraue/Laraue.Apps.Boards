using System.Collections.Concurrent;
using Google.Protobuf;
using Grpc.Core;
using Laraue.Apps.Storage.Internal.Contracts;

namespace Laraue.Apps.Boards.Services.Storage;

/// <summary>
/// Stands in for the real gRPC-backed <see cref="StorageService.StorageServiceClient"/> in tests and local runs
/// without a live Laraue.Apps.Storage ("MockExternalServices", see <c>AddStorageClient</c>): keeps files and
/// references in memory. An image gets a "preview" that is a copy of itself. Usage is the size of an owner's files and
/// a <c>quota_bytes</c> is enforced like Storage does it (no deduplication here). <see cref="Files"/> and
/// <see cref="References"/> show what was stored; <see cref="FakeStorageHttpHandler"/> serves the files' URLs.
/// </summary>
public class FakeStorageServiceClient : StorageService.StorageServiceClient
{
    public ConcurrentDictionary<Guid, FakeStoredFile> Files { get; } = new();

    /// <summary>
    /// Reference id -> (file id, confirmed).
    /// </summary>
    public ConcurrentDictionary<Guid, FakeReference> References { get; } = new();

    /// <summary>
    /// When set, the next confirm or delete throws it instead of running, then it's cleared - for retry tests.
    /// </summary>
    public RpcException? NextReferenceCallFailure { get; set; }

    public override AsyncUnaryCall<UploadFileResponse> UploadFileAsync(
        UploadFileRequest request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        var ownerId = request.Owner.OrganizationId;
        var content = request.Content.ToByteArray();
        var isImage = request.ContentType.StartsWith("image/", StringComparison.Ordinal);

        // The preview is a copy of the original, so an image takes twice its size.
        var requiredBytes = isImage ? 2L * content.Length : content.Length;
        var usageBytes = GetUsage(ownerId);
        if (request.HasQuotaBytes && usageBytes + requiredBytes > request.QuotaBytes)
        {
            return Reply(new UploadFileResponse
            {
                QuotaExceeded = new QuotaExceeded
                {
                    OwnerUsageBytes = usageBytes,
                    QuotaBytes = request.QuotaBytes,
                    RequiredBytes = requiredBytes,
                },
            });
        }

        var original = Store(ownerId, request.FileName, request.ContentType, content);
        var referenceId = Guid.CreateVersion7();
        References[referenceId] = new FakeReference(original.Id, Confirmed: false);

        var uploaded = new UploadedFile
        {
            File = ToContract(original),
            ReferenceId = referenceId.ToString(),
        };

        if (isImage)
        {
            var preview = Store(ownerId, Path.ChangeExtension(request.FileName, ".webp"), "image/webp", original.Content);
            uploaded.Preview = ToContract(preview);
        }

        uploaded.OwnerUsageBytes = GetUsage(ownerId);

        return Reply(new UploadFileResponse { Uploaded = uploaded });
    }

    public override AsyncUnaryCall<ConfirmReferenceResponse> ConfirmReferenceAsync(
        ConfirmReferenceRequest request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfFailureIsSet();

        var id = Guid.Parse(request.ReferenceId);
        if (!References.TryGetValue(id, out var reference))
            throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, $"File reference {id} is not found."));

        References[id] = reference with { Confirmed = true };
        return Reply(new ConfirmReferenceResponse());
    }

    public override AsyncUnaryCall<DeleteReferenceResponse> DeleteReferenceAsync(
        DeleteReferenceRequest request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfFailureIsSet();

        var deleted = References.TryRemove(Guid.Parse(request.ReferenceId), out var reference)
            && References.Values.All(x => x.FileId != reference.FileId)
            && Files.TryRemove(reference.FileId, out _);

        return Reply(new DeleteReferenceResponse { FileDeleted = deleted });
    }

    public override AsyncUnaryCall<GetUsageResponse> GetUsageAsync(
        GetUsageRequest request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        return Reply(new GetUsageResponse { UsageBytes = GetUsage(request.Owner.OrganizationId) });
    }

    private long GetUsage(long organizationId) => Files.Values
        .Where(x => x.OrganizationId == organizationId)
        .Sum(x => (long)x.Content.Length);

    private void ThrowIfFailureIsSet()
    {
        var failure = NextReferenceCallFailure;
        NextReferenceCallFailure = null;
        if (failure is not null)
            throw failure;
    }

    private FakeStoredFile Store(long organizationId, string fileName, string contentType, byte[] content)
    {
        var file = new FakeStoredFile(Guid.CreateVersion7(), organizationId, fileName, contentType, content);
        Files[file.Id] = file;
        return file;
    }

    private static StoredFile ToContract(FakeStoredFile file) => new()
    {
        Id = file.Id.ToString(),
        FileName = file.FileName,
        ContentType = file.ContentType,
        Size = file.Content.Length,
    };

    private static AsyncUnaryCall<T> Reply<T>(T response) => new(
        Task.FromResult(response),
        Task.FromResult(new Metadata()),
        () => global::Grpc.Core.Status.DefaultSuccess,
        () => new Metadata(),
        () => { });
}

public sealed record FakeStoredFile(Guid Id, long OrganizationId, string FileName, string ContentType, byte[] Content);

public sealed record FakeReference(Guid FileId, bool Confirmed);

/// <summary>
/// Answers requests for files' public URLs (<c>{PublicBaseUrl}/{id}</c>) from a <see cref="FakeStorageServiceClient"/>,
/// standing in for Storage's public host.
/// </summary>
public class FakeStorageHttpHandler(FakeStorageServiceClient storage) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var id = request.RequestUri!.Segments[^1];
        if (!Guid.TryParse(id, out var fileId) || !storage.Files.TryGetValue(fileId, out var file))
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));

        var content = new ByteArrayContent(file.Content);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
    }
}
