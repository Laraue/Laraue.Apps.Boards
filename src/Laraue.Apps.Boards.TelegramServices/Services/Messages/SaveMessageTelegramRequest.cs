using Laraue.Apps.Boards.Services;
using File = Laraue.Apps.Boards.Services.File;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public abstract class SaveMessageTelegramRequest
{
    public required string? From { get; set; }
    public required long ExternalChatId { get; set; }
    public required int ExternalMessageId { get; set; }
    public required Guid UserId { get; set; }
    public required string? Text { get; set; }
    public required DateTime SentAt { get; set; }
    public required string? MediaGroupId { get; set; }
}

public class SaveTextMessageTelegramRequest : SaveMessageTelegramRequest
{}

public class SaveImageMessageTelegramRequest : SaveMessageTelegramRequest
{
    public required PhotoFile[] Photos { get; set; }
}

/// <summary>
/// A file sent as a document (not as a photo or video). Saved only if it's one of the attachment file types.
/// </summary>
public class SaveDocumentMessageTelegramRequest : SaveMessageTelegramRequest
{
    public required File Document { get; set; }
}

public class SaveVideoMessageTelegramRequest : SaveMessageTelegramRequest
{
    public required File Video { get; set; }
    public required PhotoFile? Thumbnail { get; set; }
    public required int Width { get; set; }
    public required int Height { get; set; }
    public required int Duration { get; set; }
}
