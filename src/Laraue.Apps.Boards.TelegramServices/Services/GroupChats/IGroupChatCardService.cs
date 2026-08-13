using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices.Resources;
using Laraue.Apps.Boards.TelegramServices.Services.Messages;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using File = Laraue.Apps.Boards.Services.File;

namespace Laraue.Apps.Boards.TelegramServices.Services.GroupChats;

/// <summary>
/// Handles messages in group/supergroup chats: detects @mentions of the bot and turns the
/// replied-to message into a card, per the linked chat's destination.
/// </summary>
public interface IGroupChatCardService
{
    /// <summary>
    /// Entry point for every Message/EditedMessage update from a group/supergroup chat that
    /// wasn't otherwise routed (e.g. not a /link or /unlink command).
    /// </summary>
    /// <returns><c>true</c> if the message was handled (a bot mention), <c>false</c> otherwise.</returns>
    Task<bool> HandleGroupMessage(Message message, CancellationToken cancellationToken);
}

public class GroupChatCardService(
    DatabaseContext context,
    IGroupChatService groupChatService,
    ICoreUserService coreUserService,
    ICoreIssuesService coreIssuesService,
    ITelegramMessageService telegramMessageService,
    ITelegramSaveMessageService telegramSaveMessageService,
    ITelegramBotClient client,
    IDateTimeProvider dateTimeProvider,
    IOptions<AppOptions> appOptions)
    : IGroupChatCardService
{
    private const string ThumbnailMimeType = "image/jpg";

    public async Task<bool> HandleGroupMessage(Message message, CancellationToken cancellationToken)
    {
        if (!await IsBotMentioned(message, cancellationToken))
        {
            // Not a mention - if it's an album item in a linked chat, passively record its
            // attachment so a later reply+mention on any sibling item can pull it in too.
            if (message.MediaGroupId is not null)
            {
                var chatLink = await groupChatService.GetLink(message.Chat.Id, cancellationToken);
                if (chatLink is { OrganizationId: not null, SpaceId: not null })
                    await RecordAlbumItem(message, message.Chat.Id, cancellationToken);
            }

            return false;
        }

        var chatId = message.Chat.Id;
        var link = await groupChatService.GetLink(chatId, cancellationToken);

        if (link is not { OrganizationId: not null, SpaceId: not null })
        {
            await client.SendMessage(
                chatId,
                Phrases.MentionChatNotLinked,
                replyParameters: message,
                cancellationToken: cancellationToken);

            return true;
        }

        await CreateCardFromMention(message, link, cancellationToken);

        return true;
    }

    private async Task RecordAlbumItem(Message message, long chatId, CancellationToken cancellationToken)
    {
        // Text-only messages can't be an album item on their own in Telegram, but guard anyway.
        if (message.Type is not (MessageType.Photo or MessageType.Video or MessageType.Animation))
            return;

        var request = await BuildRequest(message, chatId, link: null, cancellationToken);

        if (request is not null)
            await telegramSaveMessageService.RecordAlbumItem(request, cancellationToken);
    }

    private async Task<bool> IsBotMentioned(Message message, CancellationToken cancellationToken)
    {
        if (message.Entities is null || message.Text is null)
            return false;

        var bot = await client.GetMe(cancellationToken);
        var mentionText = $"@{bot.Username}";

        return message.Entities
            .Where(e => e.Type == MessageEntityType.Mention)
            .Select(e => message.Text.Substring(e.Offset, e.Length))
            .Any(value => string.Equals(value, mentionText, StringComparison.OrdinalIgnoreCase));
    }

    private async Task CreateCardFromMention(Message mentioningMessage, LinkedTelegramChat link, CancellationToken cancellationToken)
    {
        var chatId = mentioningMessage.Chat.Id;
        var replied = mentioningMessage.ReplyToMessage;

        if (replied is null)
        {
            await client.SendMessage(
                chatId,
                Phrases.MentionReplyRequired,
                replyParameters: mentioningMessage,
                cancellationToken: cancellationToken);

            return;
        }

        if (replied.MediaGroupId is not null)
        {
            await CreateCardFromAlbum(mentioningMessage, replied, link, cancellationToken);
            return;
        }

        var request = await BuildRequest(replied, chatId, link, cancellationToken);
        if (request is null)
        {
            await client.SendMessage(
                chatId,
                Phrases.MentionUnsupportedType,
                replyParameters: mentioningMessage,
                cancellationToken: cancellationToken);

            return;
        }

        await telegramMessageService.HandleSaveMessage(request, cancellationToken);

        var issueData = await context.TelegramMessages
            .Where(x => x.ExternalMessageId == replied.MessageId && x.ExternalChatId == chatId)
            .Where(x => x.Issue != null)
            .Select(x => new
            {
                IssueNumber = x.Issue!.IssueNumber!.Number,
                SpaceKey = x.Issue.Status!.Epic!.Space!.Key,
                SpaceName = x.Issue.Status.Epic.Space.Name,
                OrganizationSlug = x.Issue.Status.Epic.Space.Organization!.Slug,
                OrganizationSlugPostfix = x.Issue.Status.Epic.Space.Organization!.SlugPostfix,
                x.Issue.Content,
            })
            .FirstOrThrowNotFoundEFAsync("Issue is not found right after it was saved", cancellationToken);

        var issueKey = $"{issueData.SpaceKey}-{issueData.IssueNumber}";
        var organizationKey = $"{issueData.OrganizationSlug}-{issueData.OrganizationSlugPostfix}";
        var issueUrl = $"{appOptions.Value.Url}/organizations/{organizationKey}/issues/{issueKey}";
        var preview = BuildContentPreview(issueData.Content);

        await client.SendMessage(
            chatId,
            string.Format(Phrases.CardCreated, issueKey, issueData.SpaceName, preview, issueUrl),
            replyParameters: mentioningMessage,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Creates (or reuses) a card for an entire album: pulls in every sibling attachment
    /// already passively recorded for the media group, not just the replied-to item.
    /// </summary>
    private async Task CreateCardFromAlbum(
        Message mentioningMessage,
        Message replied,
        LinkedTelegramChat link,
        CancellationToken cancellationToken)
    {
        var chatId = mentioningMessage.Chat.Id;

        // The replied item itself should already be passively recorded (it arrived as its
        // own update before this reply), but record it defensively in case it wasn't.
        await RecordAlbumItem(replied, chatId, cancellationToken);

        var groupId = await context.TelegramMessages
            .Where(x => x.ExternalMessageId == replied.MessageId && x.ExternalChatId == chatId)
            .Select(x => x.TelegramMediaGroupId)
            .FirstOrThrowNotFoundEFAsync("Album message is not recorded", cancellationToken);

        var groupMessages = await context.TelegramMessages
            .Where(x => x.TelegramMediaGroupId == groupId)
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.PendingContent,
                x.AttachmentId,
                OwnerId = x.Attachment != null ? (Guid?)x.Attachment.OwnerId : null,
                IssueId = x.Issue != null ? (long?)x.Issue.Id : null,
            })
            .ToListAsync(cancellationToken);

        var existingIssueId = groupMessages.Select(x => x.IssueId).FirstOrDefault(id => id is not null);
        long issueId;

        if (existingIssueId is not null)
        {
            issueId = existingIssueId.Value;
        }
        else
        {
            var first = groupMessages[0];
            var statusId = await groupChatService.GetDestinationStatusId(link, cancellationToken);
            var ownerId = first.OwnerId ?? await GetOrCreateUserId(replied.From!, cancellationToken);

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            issueId = await coreIssuesService.Create(
                ownerId,
                assigneeId: null,
                first.PendingContent,
                replied.Date,
                statusId,
                first.Id,
                attributes: [],
                newFiles: [],
                cancellationToken);

            foreach (var groupMessage in groupMessages.Where(x => x.AttachmentId is not null))
            {
                context.Add(new IssueAttachment { IssueId = issueId, AttachmentId = groupMessage.AttachmentId!.Value });
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var issueData = await context.Issues
            .Where(x => x.Id == issueId)
            .Select(x => new
            {
                IssueNumber = x.IssueNumber!.Number,
                SpaceKey = x.Status!.Epic!.Space!.Key,
                SpaceName = x.Status.Epic.Space.Name,
                OrganizationSlug = x.Status.Epic.Space.Organization!.Slug,
                OrganizationSlugPostfix = x.Status.Epic.Space.Organization!.SlugPostfix,
                x.Content,
            })
            .FirstOrThrowNotFoundEFAsync("Issue is not found right after it was saved", cancellationToken);

        var issueKey = $"{issueData.SpaceKey}-{issueData.IssueNumber}";
        var organizationKey = $"{issueData.OrganizationSlug}-{issueData.OrganizationSlugPostfix}";
        var issueUrl = $"{appOptions.Value.Url}/organizations/{organizationKey}/issues/{issueKey}";

        var photoCount = groupMessages.Count(x => x.AttachmentId is not null);
        var preview = BuildContentPreview(issueData.Content);
        if (photoCount > 1)
            preview += $" ({photoCount} photos)";

        await client.SendMessage(
            chatId,
            string.Format(Phrases.CardCreated, issueKey, issueData.SpaceName, preview, issueUrl),
            replyParameters: mentioningMessage,
            cancellationToken: cancellationToken);
    }

    private async Task<SaveMessageTelegramRequest?> BuildRequest(
        Message replied,
        long chatId,
        LinkedTelegramChat? link,
        CancellationToken cancellationToken)
    {
        var ownerId = await GetOrCreateUserId(replied.From!, cancellationToken);

        switch (replied.Type)
        {
            case MessageType.Text:
                return new SaveTextMessageTelegramRequest
                {
                    Text = replied.Text,
                    ExternalMessageId = replied.MessageId,
                    UserId = ownerId,
                    ExternalUserId = chatId,
                    SentAt = replied.Date,
                    From = replied.From?.Username,
                    MediaGroupId = replied.MediaGroupId,
                    TargetSpaceId = link?.SpaceId,
                    TargetEpicId = link?.EpicId,
                    TargetStatusId = link?.StatusId,
                };

            case MessageType.Photo:
                return new SaveImageMessageTelegramRequest
                {
                    Text = replied.Caption,
                    ExternalMessageId = replied.MessageId,
                    UserId = ownerId,
                    ExternalUserId = chatId,
                    SentAt = replied.Date,
                    From = replied.From?.Username,
                    MediaGroupId = replied.MediaGroupId,
                    TargetSpaceId = link?.SpaceId,
                    TargetEpicId = link?.EpicId,
                    TargetStatusId = link?.StatusId,
                    Photos = replied.Photo!
                        .Select(photo => new PhotoFile
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

            case MessageType.Video:
            {
                var video = replied.Video!;
                return new SaveVideoMessageTelegramRequest
                {
                    Text = replied.Caption,
                    ExternalMessageId = replied.MessageId,
                    UserId = ownerId,
                    ExternalUserId = chatId,
                    SentAt = replied.Date,
                    From = replied.From?.Username,
                    MediaGroupId = replied.MediaGroupId,
                    TargetSpaceId = link?.SpaceId,
                    TargetEpicId = link?.EpicId,
                    TargetStatusId = link?.StatusId,
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
                            MimeType = ThumbnailMimeType,
                        }
                        : null,
                    Video = new File
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

            case MessageType.Animation:
            {
                var animation = replied.Animation!;
                return new SaveVideoMessageTelegramRequest
                {
                    Text = replied.Caption,
                    ExternalMessageId = replied.MessageId,
                    UserId = ownerId,
                    ExternalUserId = chatId,
                    SentAt = replied.Date,
                    From = replied.From?.Username,
                    MediaGroupId = replied.MediaGroupId,
                    TargetSpaceId = link?.SpaceId,
                    TargetEpicId = link?.EpicId,
                    TargetStatusId = link?.StatusId,
                    Height = animation.Height,
                    Width = animation.Width,
                    Thumbnail = animation.Thumbnail is not null
                        ? new PhotoFile
                        {
                            FileSize = animation.Thumbnail.FileSize,
                            FileName = null,
                            FileId = animation.Thumbnail.FileId,
                            FileUniqueId = animation.Thumbnail.FileUniqueId,
                            Height = animation.Thumbnail.Height,
                            Width = animation.Thumbnail.Width,
                            MimeType = ThumbnailMimeType,
                        }
                        : null,
                    Video = new File
                    {
                        FileSize = animation.FileSize,
                        FileName = animation.FileName,
                        FileId = animation.FileId,
                        FileUniqueId = animation.FileUniqueId,
                        MimeType = animation.MimeType,
                    },
                    Duration = animation.Duration,
                };
            }

            default:
                return null;
        }
    }

    private async Task<Guid> GetOrCreateUserId(global::Telegram.Bot.Types.User telegramUser, CancellationToken cancellationToken)
    {
        var existingUserId = await context.Users
            .Where(u => u.TelegramId == telegramUser.Id)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingUserId is not null)
            return existingUserId.Value;

        return await coreUserService.CreateIfTelegramIdNotExists(
            new DataAccess.Models.User
            {
                TelegramId = telegramUser.Id,
                TelegramUserName = telegramUser.Username,
                TelegramLanguageCode = telegramUser.LanguageCode,
                TelegramFirstName = telegramUser.FirstName,
                TelegramLastName = telegramUser.LastName,
                CreatedAt = dateTimeProvider.UtcNow,
            },
            cancellationToken);
    }

    private static string BuildContentPreview(string? content)
    {
        const int maxLength = 200;
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        return content.Length <= maxLength
            ? content
            : content[..maxLength] + "…";
    }
}
