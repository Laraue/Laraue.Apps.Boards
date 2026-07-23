using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.Services;

public interface IFileStorage
{
    Task<bool> FileExists(
        string path,
        CancellationToken cancellationToken = default);
    
    Task<FileStream> ReadFile(
        string path,
        CancellationToken cancellationToken = default);
    
    Task WriteFile(
        string path,
        Stream content,
        IDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);
}

public class FileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    public Task<bool> FileExists(
        string path,
        CancellationToken cancellationToken = default)
    {
        var physicalPath = Path.Combine(options.Value.FilesDirectory, path);

        return Task.FromResult(System.IO.File.Exists(physicalPath));
    }

    public Task<FileStream> ReadFile(
        string path,
        CancellationToken cancellationToken = default)
    {
        var physicalPath = Path.Combine(options.Value.FilesDirectory, path);

        return Task.FromResult(System.IO.File.OpenRead(physicalPath));
    }

    public async Task WriteFile(
        string path,
        Stream content,
        IDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var physicalPath = Path.Combine(options.Value.FilesDirectory, path);
        var directory = Path.GetDirectoryName(physicalPath);
        
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var fileStream = new FileStream(
            physicalPath, 
            FileMode.Create, 
            FileAccess.Write, 
            FileShare.None, 
            81920, // 80KB buffer
            useAsync: true);
            
        await content.CopyToAsync(fileStream, cancellationToken);
    }
}