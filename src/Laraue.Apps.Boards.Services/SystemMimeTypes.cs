namespace Laraue.Apps.Boards.Services;

public static class SystemMimeTypes
{
    public static string[] Supported => Images;
    public static string[] Images = ["image/jpeg", "image/jpg", "image/png"];

    /// <summary>Maximum upload size any host should accept for a single file attachment.</summary>
    public const int MaxFileSizeBytes = 3_000_000;
}