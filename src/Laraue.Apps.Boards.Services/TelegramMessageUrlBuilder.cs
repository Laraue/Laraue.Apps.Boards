namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Builds t.me links to a Telegram message - the one place that knows how. Only supergroups (and channels) have
/// message links; ids from other chats return null.
/// </summary>
public static class TelegramMessageUrlBuilder
{
    /// <summary>
    /// Supergroup and channel ids are "-100" followed by the internal id, which is what t.me/c/ links take.
    /// </summary>
    private const long SupergroupIdOffset = -1_000_000_000_000;

    /// <summary>
    /// "https://t.me/{username}/{messageId}" for a public supergroup, "https://t.me/c/{internal id}/{messageId}"
    /// for a private one (opens for its members only), null for a basic group or a private chat - Telegram has
    /// no message links there.
    /// </summary>
    public static string? Build(long externalChatId, int externalMessageId, string? chatUsername)
    {
        if (!IsSupergroup(externalChatId))
            return null;

        return string.IsNullOrEmpty(chatUsername)
            ? $"https://t.me/c/{SupergroupIdOffset - externalChatId}/{externalMessageId}"
            : $"https://t.me/{chatUsername}/{externalMessageId}";
    }

    /// <summary>
    /// A private chat with the bot has the user's own (positive) id.
    /// </summary>
    public static bool IsPrivateChat(long externalChatId) => externalChatId > 0;

    private static bool IsSupergroup(long externalChatId) => externalChatId < SupergroupIdOffset;
}
