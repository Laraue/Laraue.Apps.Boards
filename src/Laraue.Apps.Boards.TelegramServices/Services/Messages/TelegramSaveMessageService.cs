using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Core.DataAccess.EFCore.Extensions;
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
            .Where(x => x.ExternalChatId == request.ExternalUserId)
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
        
        if (savedMessage is null)
        {
            savedMessage = new TelegramMessage
            {
                ExternalMessageId = request.ExternalMessageId,
                ExternalChatId = request.ExternalUserId,
                TelegramMediaGroupId = groupId,
            };

            context.Add(savedMessage);
            await context.SaveChangesAsync(cancellationToken);
        }
        
        var cardForMessageIsCreated = (firstGroupMessageData?.CardId).HasValue;
        if (!cardForMessageIsCreated)
        {
            var statusId = await GetStatusIdToSaveMessage(request.UserId, cancellationToken);

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            
            var issueId = await coreIssuesService.Create(
                request.UserId,
                assigneeId: null,
                request.Text,
                request.SentAt,
                statusId,
                savedMessage.Id,
                attributes: [],
                newFiles: [],
                cancellationToken);

            await UpsertMediaInfo(savedMessage.Id, issueId, request.UserId, mediaInfo, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            
            return new GetOrCreateMessageResult
            {
                Result = Result.MainMessageUpdated,
                TelegramMessageId = savedMessage.Id,
            };
        }
        
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
        
        await UpsertMediaInfo(
            savedMessage.Id,
            firstGroupMessageData!.CardId!.Value,
            request.UserId,
            mediaInfo,
            cancellationToken);
        
        return new GetOrCreateMessageResult
        {
            Result = null,
            TelegramMessageId = savedMessage.Id,
        };
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
            .Where(x => x.ExternalChatId == request.ExternalUserId)
            .Select(x => new
            {
                IssueId = x.Issue != null ? (long?)x.Issue.Id : null,
                x.Id,
            })
            .FirstOrDefaultAsync(cancellationToken);
        
        // Message is not stored, save it // TODO - store only if it is the first message
        if (savedMessage?.IssueId is null)
        {
            var statusId = await GetStatusIdToSaveMessage(request.UserId, cancellationToken);

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            TelegramMessage? telegramMessage = null;
            if (savedMessage is null)
            {
                telegramMessage = new TelegramMessage
                {
                    ExternalMessageId = request.ExternalMessageId,
                    ExternalChatId = request.ExternalUserId,
                };
                
                context.Add(telegramMessage);
                
                await context.SaveChangesAsync(cancellationToken);
            }

            var messageId = savedMessage?.Id ?? telegramMessage?.Id ?? throw new InvalidOperationException();
            var issueId = await coreIssuesService.Create(
                request.UserId,
                assigneeId: null,
                request.Text,
                request.SentAt,
                statusId,
                messageId,
                attributes: [],
                newFiles: [],
                cancellationToken);
            
            await UpsertMediaInfo(messageId, issueId, request.UserId, mediaInfo, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new GetOrCreateMessageResult
            {
                Result = Result.MainMessageCreated,
                TelegramMessageId = messageId
            };
        }
        
        await context.Issues
            .Where(x => x.TelegramMessageId == savedMessage.Id)
            .ExecuteUpdateAsync(upd => upd
                .SetProperty(x => x.Content, request.Text),
                cancellationToken);
        
        await UpsertMediaInfo(savedMessage.Id, savedMessage.IssueId.Value, request.UserId, mediaInfo, cancellationToken);
        
        return new GetOrCreateMessageResult
        {
            Result = Result.MainMessageUpdated,
            TelegramMessageId = savedMessage.Id,
        };
    }

    private async Task UpsertMediaInfo(
        long telegramMessageId,
        long issueId,
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
            
            return;
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
            
            return;
        }
        
        // Media info is created in the message
        var newEntity = new IssueAttachment
        {
            Attachment = new Attachment
            {
                FileId = mediaInfo.OriginalFileId,
                Type = mediaInfo.Type,
                PreviewFileId = mediaInfo.PreviewFileId,
                CreatedAt = dateTimeProvider.UtcNow,
                OwnerId = ownerId,
            },
            IssueId = issueId,
        };
        
        context.Add(newEntity);
        await context.SaveChangesAsync(cancellationToken);

        await context.TelegramMessages
            .Where(x => x.Id == telegramMessageId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.AttachmentId, newEntity.AttachmentId),
                cancellationToken);
    }

    private async Task<long> GetStatusIdToSaveMessage(Guid userId, CancellationToken cancellationToken)
    {
        var organizationData = await context.Organizations
            .Where(o => o.Type == OrganizationType.Personal)
            .Where(o => o.OwnerId == userId)
            .Select(o => new { o.Id })
            .FirstOrThrowNotFoundEFAsync($"Personal org is not defined for user: {userId}", cancellationToken);
        
        var statusData = await context.Statuses
            .Where(s => 
                s.Epic!.IsDefault
                && s.Epic.Space!.IsDefault
                && s.Epic.Space.OrganizationId == organizationData.Id)
            .OrderBy(s => s.SortOrder)
            .Select(s => new { s.Id })
            .FirstOrThrowNotFoundEFAsync("Status to save TG message is not defined", cancellationToken);
        
        return statusData.Id;
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