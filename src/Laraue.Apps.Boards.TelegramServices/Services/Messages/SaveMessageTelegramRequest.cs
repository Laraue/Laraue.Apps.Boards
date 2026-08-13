using Laraue.Apps.Boards.Services;
using File = Laraue.Apps.Boards.Services.File;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public abstract class SaveMessageTelegramRequest
{
    public required string? From { get; set; }
    public required long ExternalUserId { get; set; }
    public required int ExternalMessageId { get; set; }
    public required Guid UserId { get; set; }
    public required string? Text { get; set; }
    public required DateTime SentAt { get; set; }
    public required string? MediaGroupId { get; set; }

    /// <summary>
    /// Destination overrides for messages coming from a linked group chat.
    /// When null, the message is saved to the sender's personal organization
    /// (today's DM behavior). When <see cref="TargetSpaceId"/> is set, the
    /// message is saved there instead - narrowed to <see cref="TargetEpicId"/>'s
    /// default status, or <see cref="TargetStatusId"/> directly if set.
    /// </summary>
    public long? TargetSpaceId { get; set; }
    public long? TargetEpicId { get; set; }
    public long? TargetStatusId { get; set; }
}

public class SaveTextMessageTelegramRequest : SaveMessageTelegramRequest
{}

public class SaveImageMessageTelegramRequest : SaveMessageTelegramRequest
{
    public required PhotoFile[] Photos { get; set; }
}

public class SaveVideoMessageTelegramRequest : SaveMessageTelegramRequest
{
    public required File Video { get; set; }
    public required PhotoFile? Thumbnail { get; set; }
    public required int Width { get; set; }
    public required int Height { get; set; }
    public required int Duration { get; set; }
}
