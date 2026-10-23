using Microsoft.AspNetCore.Http;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public static class FormFileUtility
{
    /// <summary>The start of a JPEG file - enough for its type to be recognized by content.</summary>
    public static readonly byte[] JpegContent = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    public static IFormFile GetFormFile(string imageName)
    {
        return GetFormFile(imageName, JpegContent, "image/jpeg");
    }

    public static IFormFile GetFormFile(string fileName, byte[] content, string contentType)
    {
        return new FormFile(
            new MemoryStream(content),
            0,
            content.Length,
            "file",
            fileName)
        {
            Headers = new HeaderDictionary
            {
                ["content-type"] = contentType
            },
        };
    }
}
