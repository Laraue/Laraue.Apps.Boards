using Microsoft.AspNetCore.Http;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public static class FormFileUtility
{
    public static IFormFile GetFormFile(string imageName)
    {
        return new FormFile(
            new MemoryStream([]),
            0,
            0,
            "file",
            imageName)
        {
            Headers = new HeaderDictionary
            {
                ["content-type"] = "image/jpeg"
            },
        };
    }
}