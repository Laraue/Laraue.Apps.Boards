namespace Laraue.Apps.Boards.TelegramServices;

public class AppOptions
{
    public required string Url { get; set; }
    public required IconsUrls Icons { get; set; }
}

public class IconsUrls
{
    public required string Issue { get; set; }
    public required string Organization { get; set; }
    public required string User { get; set; }
    public required string Hint { get; set; }
    public required string Space { get; set; }
}