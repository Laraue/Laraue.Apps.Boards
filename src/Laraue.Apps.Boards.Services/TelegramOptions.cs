namespace Laraue.Apps.Boards.Services;

public class TelegramOptions
{
    /// <summary>
    /// Telegram bot token.
    /// </summary>
    public required string Token { get; set; }
    
    /// <summary>
    /// The chat to store uploaded files originals.
    /// </summary>
    public required long FilesChatId { get; set; }
}