using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.Services;

public class TelegramOptions
{
    /// <summary>
    /// Telegram bot token.
    /// </summary>
    [Required]
    public required string Token { get; set; }
    
    /// <summary>
    /// The chat to store uploaded files originals. Validated as non-zero at registration
    /// (see <c>AddCoreServices</c>) - <c>[Required]</c> can't catch a missing <see cref="long"/>.
    /// </summary>
    public required long FilesChatId { get; set; }

    /// <summary>
    /// <see cref="Token"/>, or a clear error naming the missing setting. Production validates
    /// options at startup, but other environments don't (see <c>AddValidatedOptions</c>), so a local
    /// run without <c>Telegram:Token</c> would otherwise fail deep inside Telegram.Bot with an
    /// unhelpful <see cref="ArgumentNullException"/> the first time the bot client is needed.
    /// </summary>
    public string GetRequiredToken()
    {
        return string.IsNullOrWhiteSpace(Token)
            ? throw new InvalidOperationException(
                "Telegram:Token is not configured. Set it (and Telegram:FilesChatId) in this host's " +
                "appsettings.Development.json for local runs.")
            : Token;
    }
}