using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.Services.Storage;

/// <summary>
/// Settings for calling Laraue.Apps.Storage's internal gRPC API, which keeps the files users upload (BRD-339).
/// </summary>
public class StorageOptions
{
    /// <summary>
    /// gRPC endpoint address, e.g. <c>http://localhost:5563</c> in local dev
    /// (<c>Laraue.Apps.Storage.InternalApiHost</c>'s <c>Kestrel:GrpcPort</c>).
    /// </summary>
    [Required]
    [Url]
    public required string GrpcUrl { get; set; }

    /// <summary>
    /// Where clients download files: Storage's public host, <c>https://storage.laraue.com</c>. A file's URL is this plus
    /// its Storage id (<see cref="IStorageFileUrlBuilder"/>), the same URL Storage itself returns.
    /// </summary>
    [Required]
    [Url]
    public required string PublicBaseUrl { get; set; }
}
