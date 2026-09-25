using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class UserPreferences
{
    public Guid UserId { get; set; }
    
    /// <summary>
    /// Last selected borders ordering.
    /// </summary>
    public EpicSortOrder EpicSortOrder { get; set; }

    /// <summary>
    /// Two-letter code of the language the web app and the Telegram bot talk to this user in.
    /// Seeded at sign-up from the sign-in method's language; null falls back to the default.
    /// </summary>
    [MaxLength(2)]
    public string? InterfaceLanguage { get; set; }
}

public enum EpicSortOrder
{
    LastTouched,
    Alphabetical,
}