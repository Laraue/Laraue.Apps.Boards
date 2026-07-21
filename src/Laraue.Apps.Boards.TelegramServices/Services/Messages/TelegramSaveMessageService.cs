using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Core.DataAccess.EFCore.Extensions;
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
    ICoreIssuesService coreIssuesService)
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
                SaveMessageEntity(saveTextRequest, cancellationToken),
            SaveVideoMessageTelegramRequest saveVideoRequest =>
                SaveVideoEntity(saveVideoRequest, cancellationToken),
            _ => throw new NotImplementedException(request.GetType().Name)
        };
    }


    private async Task<GetOrCreateMessageResult> SaveVideoEntity(
        SaveVideoMessageTelegramRequest request,
        CancellationToken cancellationToken)
    {
        var getOrCreateResult = await SaveMessageEntity(request, cancellationToken);
        
        long? previewFileId = null;
        if (request.Thumbnail is not null)
        {
            await coreFilesService.DownloadToLocalStorage(request.Thumbnail.FileId, request.Thumbnail.MimeType, cancellationToken);
            previewFileId = await CreateTelegramFileIfNotExists(request.Thumbnail, cancellationToken);
        }
        
        var fileId = await CreateTelegramFileIfNotExists(
            request.Video,
            cancellationToken);
        
        await context.TelegramMessages
            .Where(x => x.Id == getOrCreateResult.TelegramMessageId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.TelegramPreviewFileId, previewFileId)
                .SetProperty(p => p.TelegramFileId, fileId)
                .SetProperty(p => p.AttachmentType, AttachmentType.Video),
                cancellationToken);
        
        return getOrCreateResult;
    }

    private async Task<long> CreateTelegramFileIfNotExists(File file, CancellationToken cancellationToken)
    {
        var oldFileData = await context.TelegramFiles
            .Where(x => x.ExternalFileUniqueId == file.FileUniqueId)
            .Select(x => new { x.FileId, x.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (oldFileData is not null)
            return oldFileData.Id;
        
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

        return telegramFile.Id;
    }
    
    private async Task<GetOrCreateMessageResult> SaveImageEntity(
        SaveImageMessageTelegramRequest request,
        CancellationToken cancellationToken)
    {
        var getOrCreateResult = await SaveMessageEntity(request, cancellationToken);
        if (request.Photos.Length == 0)
            return getOrCreateResult;
        
        // If this unique file id already stored for file then skip
        // If not stored, then remove previous and store
        var thumbnailPhoto = request.Photos[0];
        var originalPhoto = request.Photos.Last();
        
        await coreFilesService.DownloadToLocalStorage(thumbnailPhoto.FileId, thumbnailPhoto.MimeType, cancellationToken);
        var thumbnailPhotoFileId = await CreateTelegramFileIfNotExists(thumbnailPhoto, cancellationToken);
        var originalPhotoFileId = await CreateTelegramFileIfNotExists(originalPhoto, cancellationToken);

        await context.TelegramMessages
            .Where(x => x.Id == getOrCreateResult.TelegramMessageId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.TelegramPreviewFileId, thumbnailPhotoFileId)
                .SetProperty(p => p.TelegramFileId, originalPhotoFileId)
                .SetProperty(p => p.AttachmentType, AttachmentType.Image),
                cancellationToken);
        
        return getOrCreateResult;
    }
        
    // New msg, old group
    // Old msg, old group
    // if first msg in group then update content
    private Task<GetOrCreateMessageResult> SaveMessageEntity(
        SaveMessageTelegramRequest request,
        CancellationToken cancellationToken)
    {
        return request.MediaGroupId == null
            ? SaveSingleMessageEntity(request, cancellationToken)
            : SaveGroupMessageEntity(request, cancellationToken);
    }

    /// <summary>
    /// Difficult case. Message is one message of the group.
    /// </summary>
    private async Task<GetOrCreateMessageResult> SaveGroupMessageEntity(
        SaveMessageTelegramRequest request,
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
            
            await coreIssuesService.Create(
                new CreateIssueRequest
                {
                    CreatedAt = request.SentAt,
                    Text = request.Text,
                    StatusId = statusId,
                    TelegramMessageId = savedMessage.Id,
                    UserId = request.UserId,
                }, cancellationToken);
            
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
            await coreIssuesService.Create(
                new CreateIssueRequest
                {
                    CreatedAt = request.SentAt,
                    Text = request.Text,
                    TelegramMessageId = messageId,
                    StatusId = statusId,
                    UserId = request.UserId,
                }, cancellationToken);

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
        
        return new GetOrCreateMessageResult
        {
            Result = Result.MainMessageUpdated,
            TelegramMessageId = savedMessage.Id,
        };
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