using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.Services.Storage;

public interface IStorageFileUrlBuilder
{
    /// <summary>
    /// The public URL of a file kept in Laraue.Apps.Storage, e.g.
    /// "https://storage.laraue.com/0195f3a2-6c1e-7b3d-9a40-2f6d8e1c5b7a" - the same URL Storage itself returns.
    /// </summary>
    string Build(Guid storageFileId);
}

public class StorageFileUrlBuilder(IOptions<StorageOptions> options) : IStorageFileUrlBuilder
{
    public string Build(Guid storageFileId)
    {
        return $"{options.Value.PublicBaseUrl.TrimEnd('/')}/{storageFileId}";
    }
}
