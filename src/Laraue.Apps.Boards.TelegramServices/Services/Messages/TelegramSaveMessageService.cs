using System.Text;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices.Services.Search;
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

    /// <summary>
    /// Manually turns an already-recorded message into a card, in response to /save. Only
    /// meaningful in <see cref="SaveMode.BotMentionedMessages"/> - <see cref="Save"/> already
    /// creates cards immediately in <see cref="SaveMode.EachMessage"/>.
    /// </summary>
    Task<SaveByReplyResult> SaveByReply(
        SaveByReplyRequest request,
        CancellationToken cancellationToken);
}

public class TelegramSaveMessageService(
    DatabaseContext context,
    ICoreFilesService coreFilesService,
    ICoreIssuesService coreIssuesService,
    IAccessService accessService,
    IIssueUrlBuilder issueUrlBuilder,
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

    public async Task<SaveByReplyResult> SaveByReply(
        SaveByReplyRequest request,
        CancellationToken cancellationToken)
    {
        var linkedChat = await GetLinkedChatToSaveMessage(request.ExternalChatId, cancellationToken);

        if (linkedChat.SaveMode == SaveMode.EachMessage)
            return new SaveByReplyResult { Outcome = SaveByReplyOutcome.NotNeededInAutoMode };

        var repliedMessage = await context.TelegramMessages
            .Where(x => x.ExternalMessageId == request.RepliedExternalMessageId)
            .Where(x => x.ExternalChatId == request.ExternalChatId)
            .Select(x => new { x.Id, x.TelegramMediaGroupId })
            .FirstOrDefaultAsyncEF(cancellationToken);

        if (repliedMessage is null)
            return new SaveByReplyResult { Outcome = SaveByReplyOutcome.MessageNotTracked };

        // Card content and attachments always live on a group's first message - whether the
        // user replied to that one specifically or another photo in the same album.
        var groupQuery = repliedMessage.TelegramMediaGroupId is null
            ? context.TelegramMessages.Where(x => x.Id == repliedMessage.Id)
            : context.TelegramMessages.Where(x => x.TelegramMediaGroupId == repliedMessage.TelegramMediaGroupId);

        var groupMessages = await groupQuery
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Text, x.AttachmentId, IssueId = x.Issue != null ? (long?)x.Issue.Id : null })
            .ToListAsyncEF(cancellationToken);

        // repliedMessage.Id is always one of these rows (either the query above matched it
        // directly, or it's a member of the group it's filtered by), so this can never be empty.
        var cardMessage = groupMessages.First();

        await EnsureCanCreateIssue(linkedChat, request.UserId, request.ExternalChatId, cancellationToken);

        var content = ComposeReplyContent(request.Note, cardMessage.Text);

        if (cardMessage.IssueId is not null)
        {
            // Manual mode never syncs edits into an existing card on its own (see Save) - a
            // repeat /save is the explicit trigger that pulls in whatever has changed since,
            // both the text and any new attachments in the group.
            if (content is not null)
            {
                await context.Issues
                    .Where(x => x.Id == cardMessage.IssueId)
                    .ExecuteUpdateAsync(upd => upd.SetProperty(x => x.Content, content), cancellationToken);
            }

            foreach (var groupMessage in groupMessages)
            {
                if (groupMessage.AttachmentId is not null)
                    await LinkAttachmentToIssue(cardMessage.IssueId.Value, groupMessage.AttachmentId.Value, cancellationToken);
            }

            var existingPreview = await GetIssuePreview(cardMessage.IssueId.Value, cancellationToken);

            return new SaveByReplyResult
            {
                Outcome = SaveByReplyOutcome.AlreadySaved,
                TelegramMessageId = cardMessage.Id,
                IssueUrl = existingPreview.Url,
                IssuePreviewText = existingPreview.Text,
            };
        }

        if (content is null)
            return new SaveByReplyResult { Outcome = SaveByReplyOutcome.NothingToSave };

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var issueId = await coreIssuesService.Create(
            request.UserId,
            assigneeId: null,
            content,
            dateTimeProvider.UtcNow,
            linkedChat.StatusId,
            cardMessage.Id,
            attributes: [],
            newFiles: [],
            cancellationToken);

        foreach (var groupMessage in groupMessages)
        {
            if (groupMessage.AttachmentId is not null)
                await LinkAttachmentToIssue(issueId, groupMessage.AttachmentId.Value, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var preview = await GetIssuePreview(issueId, cancellationToken);

        return new SaveByReplyResult
        {
            Outcome = SaveByReplyOutcome.Saved,
            TelegramMessageId = cardMessage.Id,
            IssueUrl = preview.Url,
            IssuePreviewText = preview.Text,
        };
    }

    /// <summary>
    /// Combines the user's optional /save note with the linked message's own stored text
    /// (a "---" Markdown rule between the two, when both are present).
    /// </summary>
    private static string? ComposeReplyContent(string? note, string? originalText)
    {
        note = note?.Trim();
        originalText = originalText?.Trim();

        var hasNote = !string.IsNullOrEmpty(note);
        var hasOriginalText = !string.IsNullOrEmpty(originalText);

        if (!hasNote && !hasOriginalText)
            return null;

        if (!hasNote)
            return originalText;

        if (!hasOriginalText)
            return note;

        return new StringBuilder()
            .Append(note)
            .Append("\n---\n")
            .Append(originalText)
            .ToString();
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
                Text = request.Text?.Trim(),
                SenderId = request.UserId,
                SentAt = request.SentAt,
            };

            context.Add(savedMessage);
            await context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // Keep the stored text in sync with edits, even before any card exists - /save
            // reads it later and the Bot API won't give us a second chance at it.
            await context.TelegramMessages
                .Where(x => x.Id == savedMessage.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.Text, request.Text != null ? request.Text.Trim() : null), cancellationToken);
        }

        // Record the attachment regardless of save mode - group members can arrive well before
        // any card exists for the group, e.g. waiting for /save in BotMentionedMessages mode.
        var attachmentId = await UpsertAttachment(savedMessage.Id, request.UserId, mediaInfo, cancellationToken);

        linkedChat ??= await GetLinkedChatToSaveMessage(request.ExternalChatId, cancellationToken);

        var cardForMessageIsCreated = (firstGroupMessageData?.CardId).HasValue;
        if (cardForMessageIsCreated)
        {
            // Auto-sync an edit into its card only in EachMessage mode - see the equivalent
            // comment in SaveSingleMessageEntity.
            if (linkedChat.SaveMode != SaveMode.EachMessage)
                return Recorded(savedMessage.Id);

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

        // Auto-save only happens in EachMessage mode - in BotMentionedMessages mode the group
        // is recorded but stays card-less until the user replies to one of its messages with
        // /save.
        if (linkedChat.SaveMode != SaveMode.EachMessage)
            return Recorded(savedMessage.Id);

        await EnsureCanCreateIssue(linkedChat, request.UserId, request.ExternalChatId, cancellationToken);

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
                Text = request.Text?.Trim(),
                SenderId = request.UserId,
                SentAt = request.SentAt,
            };

            context.Add(telegramMessage);
            await context.SaveChangesAsync(cancellationToken);

            messageId = telegramMessage.Id;
        }
        else
        {
            messageId = savedMessage.Id;

            // Keep the stored text in sync with edits, even before any card exists - /save
            // reads it later and the Bot API won't give us a second chance at it.
            await context.TelegramMessages
                .Where(x => x.Id == messageId)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.Text, request.Text != null ? request.Text.Trim() : null), cancellationToken);
        }

        // Record the attachment regardless of save mode - a message can exist (and be edited)
        // well before it becomes a card, e.g. waiting for /save in BotMentionedMessages mode.
        var attachmentId = await UpsertAttachment(messageId, request.UserId, mediaInfo, cancellationToken);

        linkedChat ??= await GetLinkedChatToSaveMessage(request.ExternalChatId, cancellationToken);

        if (savedMessage?.IssueId is not null)
        {
            // Auto-sync an edit into its card only in EachMessage mode. In BotMentionedMessages
            // mode the card was created deliberately via /save, so edits shouldn't silently
            // overwrite it - the user has to /save again to pull in changes.
            if (linkedChat.SaveMode != SaveMode.EachMessage)
                return Recorded(savedMessage.Id);

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

        // Auto-save only happens in EachMessage mode - in BotMentionedMessages mode the message
        // is recorded but stays card-less until the user replies with /save.
        if (linkedChat.SaveMode != SaveMode.EachMessage)
            return Recorded(messageId);

        await EnsureCanCreateIssue(linkedChat, request.UserId, request.ExternalChatId, cancellationToken);

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
                EpicId = x.Status!.EpicId,
                OrganizationId = x.Status.Epic!.Space!.OrganizationId,
                SaveMode = x.SaveMode,
            })
            .FirstOrDefaultAsyncEF(cancellationToken);

        return linkedChatData ?? throw new ChatNotLinkedException(externalChatId);
    }

    /// <summary>
    /// Builds the same key/org/content-preview + link shown for an inline search result, so a
    /// /save or /info reply looks like the same "card" wherever it's shown from.
    /// </summary>
    private async Task<(string Text, string Url)> GetIssuePreview(long issueId, CancellationToken cancellationToken)
    {
        var issueData = await context.Issues
            .Where(x => x.Id == issueId)
            .Select(x => new
            {
                Key = new IssueKey(x.IssueNumber!.Space!.Key, x.IssueNumber.Number),
                OrganizationName = x.IssueNumber.Space.Organization!.Name,
                OrganizationSlug = x.IssueNumber.Space.Organization!.Slug,
                OrganizationSlugPostfix = x.IssueNumber.Space.Organization!.SlugPostfix,
                x.Content,
                ChatTitle = x.TelegramMessage != null ? x.TelegramMessage.LinkedTelegramChat!.Title : null,
                SenderName = x.TelegramMessage != null && x.TelegramMessage.Sender != null
                    ? (x.TelegramMessage.Sender.TelegramUserName ?? x.TelegramMessage.Sender.TelegramFirstName)
                    : null,
                SentAt = x.TelegramMessage != null ? x.TelegramMessage.SentAt : null,
            })
            .FirstAsyncEF(cancellationToken);

        var url = issueUrlBuilder.Build(issueData.OrganizationSlug, issueData.OrganizationSlugPostfix, issueData.Key);

        var fragment = ContentFragment.Extract(
            issueData.Content ?? string.Empty,
            searchText: string.Empty,
            IssuePreviewFormatter.FragmentContextChars);

        var footer = IssuePreviewFormatter.BuildSourceFooter(issueData.ChatTitle, issueData.SenderName, issueData.SentAt);

        var text = IssuePreviewFormatter.BuildHeader(issueData.Key, issueData.OrganizationName) + "\n" + fragment.ToMarkdownV2();
        if (footer is not null)
            text += "\n" + footer;

        return (text, url);
    }

    /// <summary>
    /// Guards issue creation: the chat being linked to an organization doesn't by itself grant
    /// the person typing in it permission to create cards there.
    /// </summary>
    private async Task EnsureCanCreateIssue(
        LinkedChatToSaveMessage linkedChat,
        Guid userId,
        long externalChatId,
        CancellationToken cancellationToken)
    {
        var authData = new OrganizationAuthData { OrganizationId = linkedChat.OrganizationId, UserId = userId };
        var accessLevels = await accessService.GetAccessLevelsByEpicId(authData, linkedChat.EpicId, cancellationToken);

        if (accessLevels?.CanCreateIssue != true)
            throw new IssueCreationForbiddenException(externalChatId);
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
    public required long EpicId { get; init; }
    public required long OrganizationId { get; init; }
    public required SaveMode SaveMode { get; init; }
}

public class SaveByReplyRequest
{
    public required long ExternalChatId { get; init; }
    public required int RepliedExternalMessageId { get; init; }
    public required Guid UserId { get; init; }

    /// <summary>
    /// Extra text typed after /save, e.g. "/save this one" -&gt; "this one". Null when the
    /// command was sent bare.
    /// </summary>
    public required string? Note { get; init; }
}

public class SaveByReplyResult
{
    public required SaveByReplyOutcome Outcome { get; init; }
    public long? TelegramMessageId { get; init; }

    /// <summary>
    /// Set when <see cref="Outcome"/> is <see cref="SaveByReplyOutcome.Saved"/> or
    /// <see cref="SaveByReplyOutcome.AlreadySaved"/> - both have a card to link to.
    /// </summary>
    public string? IssueUrl { get; init; }

    /// <summary>
    /// MarkdownV2 "📋 KEY · Org\n{content preview}" text, matching the inline search result
    /// format. Set alongside <see cref="IssueUrl"/>.
    /// </summary>
    public string? IssuePreviewText { get; init; }
}

public enum SaveByReplyOutcome
{
    Saved,

    /// <summary>The chat is in EachMessage mode, where /save has nothing to do.</summary>
    NotNeededInAutoMode,

    /// <summary>The replied-to message was never recorded by the bot.</summary>
    MessageNotTracked,

    /// <summary>
    /// The replied-to message (or its group) already has a card - its content and any new
    /// attachments were just re-synced from the current message state.
    /// </summary>
    AlreadySaved,

    /// <summary>Neither the replied-to message nor the /save note had any content.</summary>
    NothingToSave,
}
