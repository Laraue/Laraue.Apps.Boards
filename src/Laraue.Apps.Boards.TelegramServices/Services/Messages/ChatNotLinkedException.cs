namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

/// <summary>
/// Thrown when a message arrives from a chat that has no active <see cref="DataAccess.Models.LinkedTelegramChat"/>,
/// so there is nowhere to save it. Handled by callers that have access to the bot client to notify the user.
/// </summary>
public class ChatNotLinkedException(long externalChatId)
    : Exception($"Chat is not linked: {externalChatId}")
{
    public long ExternalChatId { get; } = externalChatId;
}
