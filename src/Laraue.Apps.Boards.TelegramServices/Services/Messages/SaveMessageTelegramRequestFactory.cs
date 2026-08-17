using Laraue.Apps.Boards.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Services_File = Laraue.Apps.Boards.Services.File;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

/// <summary>
/// Builds a <see cref="SaveMessageTelegramRequest"/> from an incoming Telegram message.
/// Shared by private-chat and group-chat message handling so both go through the exact same
/// save pipeline (SaveMode, chat linking, attachments).
/// </summary>
public static class SaveMessageTelegramRequestFactory
{
    private const string ThumbnailMimeType = "image/jpg";

    public static SaveMessageTelegramRequest? Create(Message message, Guid userId, long externalChatId)
    {
        return message.Type switch
        {
            MessageType.Text => GetMessageRequest(message, userId, externalChatId),
            MessageType.Photo => GetPhotoRequest(message, userId, externalChatId),
            MessageType.Video => GetVideoRequest(message, userId, externalChatId),
            MessageType.Animation => GetAnimationRequest(message, userId, externalChatId),
            _ => null
        };
    }

    private static SaveTextMessageTelegramRequest GetMessageRequest(Message message, Guid userId, long externalChatId)
    {
        return new SaveTextMessageTelegramRequest
        {
            Text = message.Text,
            ExternalMessageId = message.MessageId,
            UserId = userId,
            ExternalChatId = externalChatId,
            SentAt = message.Date,
            From = message.From?.Username,
            MediaGroupId = message.MediaGroupId,
        };
    }

    private static SaveImageMessageTelegramRequest GetPhotoRequest(Message message, Guid userId, long externalChatId)
    {
        return new SaveImageMessageTelegramRequest
        {
            Text = message.Caption,
            ExternalMessageId = message.MessageId,
            UserId = userId,
            ExternalChatId = externalChatId,
            SentAt = message.Date,
            From = message.From?.Username,
            MediaGroupId = message.MediaGroupId,
            Photos = message.Photo!
                .Select(photo => new PhotoFile()
                {
                    FileId = photo.FileId,
                    FileUniqueId = photo.FileUniqueId,
                    Height = photo.Height,
                    Width = photo.Width,
                    FileSize = photo.FileSize,
                    FileName = null,
                    MimeType = ThumbnailMimeType,
                })
                .ToArray(),
        };
    }

    private static SaveVideoMessageTelegramRequest GetVideoRequest(Message message, Guid userId, long externalChatId)
    {
        var video = message.Video!;

        return new SaveVideoMessageTelegramRequest
        {
            Text = message.Caption,
            ExternalMessageId = message.MessageId,
            UserId = userId,
            ExternalChatId = externalChatId,
            SentAt = message.Date,
            From = message.From?.Username,
            MediaGroupId = message.MediaGroupId,
            Height = video.Height,
            Width = video.Width,
            Thumbnail = video.Thumbnail is not null
                ? new PhotoFile
                {
                    FileSize = video.Thumbnail.FileSize,
                    FileName = null,
                    FileId = video.Thumbnail.FileId,
                    FileUniqueId = video.Thumbnail.FileUniqueId,
                    Height = video.Thumbnail.Height,
                    Width = video.Thumbnail.Width,
                    MimeType = ThumbnailMimeType
                } : null,
            Video = new Services_File()
            {
                FileSize = video.FileSize,
                FileName = video.FileName,
                FileId = video.FileId,
                FileUniqueId = video.FileUniqueId,
                MimeType = video.MimeType,
            },
            Duration = video.Duration,
        };
    }

    private static SaveVideoMessageTelegramRequest GetAnimationRequest(Message message, Guid userId, long externalChatId)
    {
        var video = message.Animation!;

        return new SaveVideoMessageTelegramRequest
        {
            Text = message.Caption,
            ExternalMessageId = message.MessageId,
            UserId = userId,
            ExternalChatId = externalChatId,
            SentAt = message.Date,
            From = message.From?.Username,
            MediaGroupId = message.MediaGroupId,
            Height = video.Height,
            Width = video.Width,
            Thumbnail = video.Thumbnail is not null
                ? new PhotoFile
                {
                    FileSize = video.Thumbnail.FileSize,
                    FileName = null,
                    FileId = video.Thumbnail.FileId,
                    FileUniqueId = video.Thumbnail.FileUniqueId,
                    Height = video.Thumbnail.Height,
                    Width = video.Thumbnail.Width,
                    MimeType = ThumbnailMimeType
                } : null,
            Video = new Services_File
            {
                FileSize = video.FileSize,
                FileName = video.FileName,
                FileId = video.FileId,
                FileUniqueId = video.FileUniqueId,
                MimeType = video.MimeType,
            },
            Duration = video.Duration,
        };
    }
}
