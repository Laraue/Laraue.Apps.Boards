using System.Globalization;

namespace Laraue.Apps.Boards.Services.Storage;

/// <summary>
/// Writes a size in bytes the way people read it in a message: "512 KB", "98.5 MB", "1 GB".
/// </summary>
public static class StorageSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        double value = bytes;
        var unit = 0;
        while (Math.Abs(value) >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{Math.Round(value, 1).ToString(CultureInfo.InvariantCulture)} {Units[unit]}";
    }
}
