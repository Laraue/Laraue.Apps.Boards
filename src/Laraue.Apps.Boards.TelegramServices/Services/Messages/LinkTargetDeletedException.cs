namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

/// <summary>
/// Thrown when a card would be created in a linked chat whose target status was deleted - with its space or board,
/// since spaces can be deleted (BRD-293). Handled by callers that have access to the bot client to notify the user.
/// </summary>
public class LinkTargetDeletedException(long externalChatId)
    : Exception($"The status chat {externalChatId} saves to is deleted")
{
    public long ExternalChatId { get; } = externalChatId;
}
