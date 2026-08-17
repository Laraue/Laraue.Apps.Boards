using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Core.DateTime.Services.Abstractions;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using File = Laraue.Apps.Boards.Services.File;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public interface ITelegramSaveMessageService
{
    Task<GetOrCreateMessageResult> Save(
        SaveMessageTelegramRequest request,
        CancellationToken cancellationToken);
}

public class TelegramSaveMessageService(
    DatabaseContext context,
    ICoreFilesService coreFilesService,
    ICoreIssuesService coreIssuesService,
    IDateTimeProvider dateTimeProvider)
    : ITelegramSaveMessageService
{
    public Task<GetOrCreateMessageResult> Save(
        SaveMessageTelegramRequest request,
        CancellationToken cancellationToken)
    {
        return request switch
        {
            SaveImageMessageTelegramRequest saveImageRequest =>
                SaveImageEntity(saveImageRequest, cancellationToken),
            SaveTextMessageTelegramRequest saveTextRequest =>
                SaveMessageEntity(saveTextRequest, null, cancellationToken),
            SaveVideoMessageTelegramRequest saveVideoRequest =>
                SaveVideoEntity(saveVideoRequest, cancellationToken),
            _ => throw new NotImplementedException(request.GetType().Name)
        };
    }


    private async Task<GetOrCreateMessageResult> SaveVideoEntity(
        SaveVideoMessageTelegramRequest request,
        CancellationToken cancellationToken)
    {
        Guid? previewFileId = null;
        if (request.Thumbnail is not null)
        {
            await coreFilesService.DownloadToLocalStorage(request.Thumbnail.FileId, request.Thumbnail.MimeType, cancellationToken);
            previewFileId = await CreateTelegramFileIfNotExists(request.Thumbnail, cancellationToken);
        }
        
        var fileId = await CreateTelegramFileIfNotExists(
            request.Video,
            cancellationToken);

        var mediaInfo = new MediaInfo
        {
            PreviewFileId = previewFileId,
            OriginalFileId = fileId,
            Type = AttachmentType.Video,
            FileName = request.Video.FileName,
        };
        
        return await SaveMessageEntity(request, mediaInfo, cancellationToken);
    }

    private async Task<Guid> CreateTelegramFileIfNotExists(File file, CancellationToken cancellationToken)
    {
        var oldFileData = await context.TelegramFiles
            .Where(x => x.ExternalFileUniqueId == file.FileUniqueId)
            .Select(x => new { x.FileId, x.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (oldFileData is not null)
            return oldFileData.FileId;
        
        var telegramFile = new TelegramFile
        {
            ExternalFileId = file.FileId,
            ExternalFileUniqueId = file.FileUniqueId,
            File = new DataAccess.Models.File
            {
                Name = file.FileName,
                Size = file.FileSize,
                MimeType = file.MimeType,
            }
        };
            
        context.Add(telegramFile);
        await context.SaveChangesAsync(cancellationToken);

        return telegramFile.FileId;
    }
    
    private async Task<GetOrCreateMessageResult> SaveImageEntity(
        SaveImageMessageTelegramRequest request,
        CancellationToken cancellationToken)
    {
        var thumbnailPhoto = request.Photos[0];
        var originalPhoto = request.Photos.Last();
        
        await coreFilesService.DownloadToLocalStorage(thumbnailPhoto.FileId, thumbnailPhoto.MimeType, cancellationToken);
        var thumbnailPhotoFileId = await CreateTelegramFileIfNotExists(thumbnailPhoto, cancellationToken);
        var originalPhotoFileId = await CreateTelegramFileIfNotExists(originalPhoto, cancellationToken);

        var mediaInfo = new MediaInfo
        {
            PreviewFileId = thumbnailPhotoFileId,
            OriginalFileId = originalPhotoFileId,
            Type = AttachmentType.Image,
            FileName = originalPhoto.FileName,
        };
        
        return await SaveMessageEntity(request, mediaInfo, cancellationToken);
    }
        
    // New msg, old group
    // Old msg, old group
    // if first msg in group then update content
    private Task<GetOrCreateMessageResult> SaveMessageEntity(
        SaveMessageTelegramRequest request,
        MediaInfo? mediaInfo,
        CancellationToken cancellationToken)
    {
        return request.MediaGroupId == null
            ? SaveSingleMessageEntity(request, mediaInfo, cancellationToken)
            : SaveGroupMessageEntity(request, mediaInfo, cancellationToken);
    }

    /// <summary>
    /// Difficult case. Message is one message of the group.
    /// </summary>
    private async Task<GetOrCreateMessageResult> SaveGroupMessageEntity(
        SaveMessageTelegramRequest request,
        MediaInfo? mediaInfo,
        CancellationToken cancellationToken)
    {
        // Try to find the message
        var savedMessage = await context.TelegramMessages
            .Where(x => x.ExternalMessageId == request.ExternalMessageId)
            .Where(x => x.ExternalChatId == request.ExternalChatId)
            .FirstOrDefaultAsync(cancellationToken);
        
        // When group id already stored in message - remain it as is. It can't change
        // Otherwise, save it if it was presented.
        var groupId = savedMessage?.TelegramMediaGroupId;
        if (groupId is null && request.MediaGroupId is not null)
            groupId = await GetOrCreateTelegramMediaGroupId(
                request.MediaGroupId,
                cancellationToken);

        // When it is the message group, only the first message content is stored to card
        var firstGroupMessageData = await context.TelegramMessages
            .Where(x => x.TelegramMediaGroupId == groupId)
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                CardId = x.Issue == null ? (long?)null : x.Issue.Id
            })
            .FirstOrDefaultAsyncEF(cancellationToken);
        
        LinkedChatToSaveMessage? linkedChat = null;

        if (savedMessage is null)
        {
            linkedChat = await GetLinkedChatToSaveMessage(request.ExternalChatId, cancellationToken);

            savedMessage = new TelegramMessage
            {
                ExternalMessageId = request.ExternalMessageId,
                ExternalChatId = request.ExternalChatId,
                TelegramMediaGroupId = groupId,
                LinkedTelegramChatId = linkedChat.LinkedTelegramChatId,
            };

            context.Add(savedMessage);
            await context.SaveChangesAsync(cancellationToken);
        }

        // Record the attachment regardless of save mode - group members can arrive well before
        // any card exists for the group, e.g. waiting for /save in BotMentionedMessages mode.
        var attachmentId = await UpsertAttachment(savedMessage.Id, request.UserId, mediaInfo, cancellationToken);

        var cardForMessageIsCreated = (firstGroupMessageData?.CardId).HasValue;
        if (cardForMessageIsCreated)
        {
            // The case when first message was deleted and text added to the second
            if (request.Text is not null && firstGroupMessageData is not null)
            {
                // TODO - here we can detect and remove previous messages. But should we?
                await context.Issues
                    .Where(x => x.Id == firstGroupMessageData.CardId)
                    .ExecuteUpdateAsync(upd => upd
                            .SetProperty(x => x.Content, request.Text),
                        cancellationToken);

                return new GetOrCreateMessageResult
                {
                    Result = Result.MainMessageUpdated,
                    TelegramMessageId = savedMessage.Id,
                };
            }

            if (attachmentId is not null)
                await LinkAttachmentToIssue(firstGroupMessageData!.CardId!.Value, attachmentId.Value, cancellationToken);

            return Recorded(savedMessage.Id);
        }

        linkedChat ??= await GetLinkedChatToSaveMessage(request.ExternalChatId, cancellationToken);

        // Auto-save only happens in EachMessage mode - in BotMentionedMessages mode the group
        // is recorded but stays card-less until the user replies to one of its messages with
        // /save.
        if (linkedChat.SaveMode != SaveMode.EachMessage)
            return Recorded(savedMessage.Id);

        return await CreateCard(
            request,
            linkedChat,
            savedMessage.Id,
            attachmentId,
            Result.MainMessageUpdated,
            cancellationToken);
    }
    
    /// <summary>
    /// Simple case. One message without groups.
    /// </summary>
    private async Task<GetOrCreateMessageResult> SaveSingleMessageEntity(
        SaveMessageTelegramRequest request,
        MediaInfo? mediaInfo,
        CancellationToken cancellationToken)
    {
        // Try to find the message
        var savedMessage = await context.TelegramMessages
            .Where(x => x.ExternalMessageId == request.ExternalMessageId)
            .Where(x => x.ExternalChatId == request.ExternalChatId)
            .Select(x => new
            {
                IssueId = x.Issue != null ? (long?)x.Issue.Id : null,
                x.Id,
            })
            .FirstOrDefaultAsync(cancellationToken);

        LinkedChatToSaveMessage? linkedChat = null;
        long messageId;

        if (savedMessage is null)
        {
            linkedChat = await GetLinkedChatToSaveMessage(request.ExternalChatId, cancellationToken);

            var telegramMessage = new TelegramMessage
            {
                ExternalMessageId = request.ExternalMessageId,
                ExternalChatId = request.ExternalChatId,
                LinkedTelegramChatId = linkedChat.LinkedTelegramChatId,
            };

            context.Add(telegramMessage);
            await context.SaveChangesAsync(cancellationToken);

            messageId = telegramMessage.Id;
        }
        else
        {
            messageId = savedMessage.Id;
        }

        // Record the attachment regardless of save mode - a message can exist (and be edited)
        // well before it becomes a card, e.g. waiting for /save in BotMentionedMessages mode.
        var attachmentId = await UpsertAttachment(messageId, request.UserId, mediaInfo, cancellationToken);

        if (savedMessage?.IssueId is not null)
        {
            await context.Issues
                .Where(x => x.TelegramMessageId == savedMessage.Id)
                .ExecuteUpdateAsync(upd => upd
                    .SetProperty(x => x.Content, request.Text),
                    cancellationToken);

            if (attachmentId is not null)
                await LinkAttachmentToIssue(savedMessage.IssueId.Value, attachmentId.Value, cancellationToken);

            return new GetOrCreateMessageResult
            {
                Result = Result.MainMessageUpdated,
                TelegramMessageId = savedMessage.Id,
            };
        }

        linkedChat ??= await GetLinkedChatToSaveMessage(request.ExternalChatId, cancellationToken);

        // Auto-save only happens in EachMessage mode - in BotMentionedMessages mode the message
        // is recorded but stays card-less until the user replies with /save.
        if (linkedChat.SaveMode != SaveMode.EachMessage)
            return Recorded(messageId);

        return await CreateCard(
            request,
            linkedChat,
            messageId,
            attachmentId,
            Result.MainMessageCreated,
            cancellationToken);
    }

    /// <summary>
    /// Turns a recorded message into a card: creates the <see cref="Issue"/> and links its
    /// already-stored attachment (if any), atomically. Shared by the EachMessage auto-save
    /// path for both single and grouped messages.
    /// </summary>
    private async Task<GetOrCreateMessageResult> CreateCard(
        SaveMessageTelegramRequest request,
        LinkedChatToSaveMessage linkedChat,
        long telegramMessageId,
        Guid? attachmentId,
        Result result,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var issueId = await coreIssuesService.Create(
            request.UserId,
            assigneeId: null,
            request.Text,
            request.SentAt,
            linkedChat.StatusId,
            telegramMessageId,
            attributes: [],
            newFiles: [],
            cancellationToken);

        if (attachmentId is not null)
            await LinkAttachmentToIssue(issueId, attachmentId.Value, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new GetOrCreateMessageResult
        {
            Result = result,
            TelegramMessageId = telegramMessageId,
        };
    }

    /// <summary>
    /// A message that's been persisted (and had its attachment recorded) but has no card yet -
    /// either it's still waiting on /save in BotMentionedMessages mode, or its card already
    /// exists and this update only touched the attachment.
    /// </summary>
    private static GetOrCreateMessageResult Recorded(long telegramMessageId) => new()
    {
        Result = null,
        TelegramMessageId = telegramMessageId,
    };

    /// <summary>
    /// Persists the raw <see cref="Attachment"/> for a message, independent of whether a card
    /// exists for it yet. In BotMentionedMessages mode messages are recorded before any card
    /// is created, so attachment storage can't be tied to issue creation the way it used to be.
    /// </summary>
    private async Task<Guid?> UpsertAttachment(
        long telegramMessageId,
        Guid ownerId,
        MediaInfo? mediaInfo,
        CancellationToken cancellationToken)
    {
        // Media info is deleted from the message
        if (mediaInfo is null)
        {
            await context.TelegramMessages
                .Where(x => x.Id == telegramMessageId)
                .Select(x => x.Attachment)
                .ExecuteDeleteAsync(cancellationToken);

            return null;
        }

        var attachmentData = await context.TelegramMessages
            .Where(x => x.Id == telegramMessageId)
            .Select(x => new
            {
                AttachmentId = x.Attachment != null ? x.Attachment.Id : (Guid?)null
            })
            .FirstAsyncEF(cancellationToken);

        // Media info is updated in the message
        if (attachmentData.AttachmentId is not null)
        {
            await context.Attachments
                .Where(x => x.Id == attachmentData.AttachmentId)
                .ExecuteUpdateAsync(upd => upd
                    .SetProperty(x => x.PreviewFileId, mediaInfo.PreviewFileId)
                    .SetProperty(x => x.FileId, mediaInfo.OriginalFileId)
                    .SetProperty(x => x.Type, mediaInfo.Type)
                    .SetProperty(x => x.CreatedAt, dateTimeProvider.UtcNow),
                    cancellationToken);

            return attachmentData.AttachmentId;
        }

        // Media info is created for the message
        var attachment = new Attachment
        {
            FileId = mediaInfo.OriginalFileId,
            Type = mediaInfo.Type,
            PreviewFileId = mediaInfo.PreviewFileId,
            CreatedAt = dateTimeProvider.UtcNow,
            OwnerId = ownerId,
        };

        context.Add(attachment);
        await context.SaveChangesAsync(cancellationToken);

        await context.TelegramMessages
            .Where(x => x.Id == telegramMessageId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.AttachmentId, attachment.Id),
                cancellationToken);

        return attachment.Id;
    }

    /// <summary>
    /// Attaches an already-stored <see cref="Attachment"/> to a card, the first time only. An
    /// attachment is linked exactly once, at the moment it's first associated with a card -
    /// matches the pre-existing behaviour where updating an attachment's file in place never
    /// re-pointed it at a different issue (an attachment can only ever belong to one issue, per
    /// the unique index on IssueAttachment.AttachmentId). Separate from
    /// <see cref="UpsertAttachment"/> because a message's attachment can exist well before its
    /// card does (BotMentionedMessages mode).
    /// </summary>
    private async Task LinkAttachmentToIssue(long issueId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var alreadyLinked = await context.IssueAttachments
            .AnyAsyncEF(x => x.AttachmentId == attachmentId, cancellationToken);

        if (alreadyLinked)
            return;

        context.Add(new IssueAttachment { IssueId = issueId, AttachmentId = attachmentId });
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<LinkedChatToSaveMessage> GetLinkedChatToSaveMessage(long externalChatId, CancellationToken cancellationToken)
    {
        var linkedChatData = await context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == externalChatId && x.UnlinkedAt == null)
            .Select(x => new LinkedChatToSaveMessage
            {
                LinkedTelegramChatId = x.Id,
                StatusId = x.StatusId,
                SaveMode = x.SaveMode,
            })
            .FirstOrDefaultAsyncEF(cancellationToken);

        return linkedChatData ?? throw new ChatNotLinkedException(externalChatId);
    }
    
    private async Task<long> GetOrCreateTelegramMediaGroupId(
        string groupId,
        CancellationToken cancellationToken)
    {
        var data = await context.TelegramMediaGroups
            .Where(x => x.ExternalId == groupId)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (data is not null)
            return data.Id;

        var group = new TelegramMediaGroup
        {
            ExternalId = groupId,
        };
        
        context.Add(group);
        await context.SaveChangesAsync(cancellationToken);
        
        return group.Id;
    }
}

public class GetOrCreateMessageResult
{
    public required long TelegramMessageId { get; set; }
    public required Result? Result { get; set; }
}

public enum Result
{
    MainMessageCreated,
    MainMessageUpdated,
}

internal class LinkedChatToSaveMessage
{
    public required long LinkedTelegramChatId { get; init; }
    public required long StatusId { get; init; }
    public required SaveMode SaveMode { get; init; }
}
