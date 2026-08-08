using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramHost.Resources;
using Laraue.Apps.Boards.TelegramServices;
using Laraue.Apps.Boards.TelegramServices.Services.Messages;
using Laraue.Telegram.NET.Abstractions;
using Laraue.Telegram.NET.Core.Extensions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Services_File = Laraue.Apps.Boards.Services.File;

namespace Laraue.Apps.Boards.TelegramHost;

public class HandleAllMessagesMiddleware(
    RequestContext context,
    ITelegramMessageService telegramMessageService,
    ITelegramBotClient botClient)
    : ITelegramMiddleware
{
    private const string ThumbnailMimeType = "image/jpg";

    private static readonly UpdateType[] AllowedUpdates =
    [
        UpdateType.Message,
        UpdateType.EditedMessage,
    ];
    
    public async Task InvokeAsync(Func<CancellationToken, Task> next, CancellationToken ct)
    {
        await next(ct);
        
        if (context.GetExecutedRoute() is null && AllowedUpdates.Contains(context.Update.Type))
        {
            var message = context.Update.Message ?? context.Update.EditedMessage;
            
            if (message!.ViaBot is not null)
            {
                // This message was produced by the user picking an inline query result
                // (@yourbot ...), not typed directly — Telegram sets ViaBot for those.
                // Nothing to save here; the search flow already handled it when it built
                // the InlineQueryResult in the first place.
                return;
            }
            
            var text = message.Text;

            SaveMessageTelegramRequest? request = message.Type switch
            {
                MessageType.Text => GetMessageRequest(message),
                MessageType.Photo => GetPhotoRequest(message),
                MessageType.Video => GetVideoRequest(message),
                MessageType.Animation => GetAnimationRequest(message),
                _ => null
            };

            if (request is not null)
            {
                await telegramMessageService.HandleSaveMessage(request, ct);
            }
            else
            {
                await botClient.SendMessage(
                    context.Update.GetUserId(),
                    string.Format(Phrases.MessageTypeIsNotAvailable, message.Type),
                    cancellationToken: ct);
            }
            
            context.SetExecutedRoute(
                new ExecutedRouteInfo("HandleAllMessagesMiddleware", text));
        }
    }
    
    private SaveTextMessageTelegramRequest GetMessageRequest(Message message)
    {
        var text = message.Text;
        
        return new SaveTextMessageTelegramRequest
        {
            Text = text,
            ExternalMessageId = message.MessageId,
            UserId = context.UserId,
            ExternalUserId = context.Update.GetUserId(),
            SentAt = message.Date,
            From = message.From?.Username,
            MediaGroupId = message.MediaGroupId,
        };
    }

    private SaveImageMessageTelegramRequest GetPhotoRequest(Message message)
    {
        var text = message.Caption;
        return new SaveImageMessageTelegramRequest
        {
            Text = text,
            ExternalMessageId = message.MessageId,
            UserId = context.UserId,
            ExternalUserId = context.Update.GetUserId(),
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
    
    private SaveVideoMessageTelegramRequest GetVideoRequest(Message message)
    {
        var text = message.Caption;
        var video = message.Video!;
        
        return new SaveVideoMessageTelegramRequest
        {
            Text = text,
            ExternalMessageId = message.MessageId,
            UserId = context.UserId,
            ExternalUserId = context.Update.GetUserId(),
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
    
    private SaveVideoMessageTelegramRequest GetAnimationRequest(Message message)
    {
        var text = message.Caption;
        var video = message.Animation!;
        
        return new SaveVideoMessageTelegramRequest
        {
            Text = text,
            ExternalMessageId = message.MessageId,
            UserId = context.UserId,
            ExternalUserId = context.Update.GetUserId(),
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