using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.Services;

public class FileStorageOptions
{
    [Required]
    public required string FilesDirectory { get; set; }
}