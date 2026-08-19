namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

/// <summary>
/// Thrown when the acting user doesn't have permission to create issues in the organization a
/// linked chat points to. Handled by callers that have access to the bot client to notify the
/// user.
/// </summary>
public class IssueCreationForbiddenException(long externalChatId)
    : Exception($"User is not allowed to create issues via chat: {externalChatId}")
{
    public long ExternalChatId { get; } = externalChatId;
}
