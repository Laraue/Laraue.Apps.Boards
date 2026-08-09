using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using User = Telegram.Bot.Types.User;

namespace Laraue.Apps.Boards.IntegrationTests;

public class TelegramHostTests : TelegramIntegrationTest
{
    [Fact]
    public async Task NewMessage_ShouldInitializeUser_Always()
    {
        using var host = GetTelegramTestHost();

        await host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = new User
                {
                    Id = 777,
                    Username = "snake991",
                },
                Id = 1,
                Text = "Test message",
            }
        });

        var scope = host.CreateScope();
        var db = scope.GetDatabaseContext();
        
        var user = Assert.Single(await db.Users.ToListAsyncLinqToDB());
        
        Assert.Equal(777, user.TelegramId);
        Assert.Equal("snake991", user.TelegramUserName);
        
        var userOrganization = Assert.Single(await db.Organizations.ToListAsyncLinqToDB());
        Assert.Equal("snake991", userOrganization.Slug);
        Assert.Equal(OrganizationType.Personal, userOrganization.Type);
    }
    
    [Fact]
    public async Task HandleTextMessage_ShouldCreateAndEditIssue_Always()
    {
        using var host = GetTelegramTestHost();

        await host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = DefaultUser,
                Id = 1,
                Text = "Test message",
            }
        });

        var scope = host.CreateScope();
        var db = scope.GetDatabaseContext();
        
        var issue = Assert.Single(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal("Test message", issue.Content);
        
        await host.SendUpdateAsync(new Update
        {
            EditedMessage = new Message
            {
                From = DefaultUser,
                Id = 1,
                Text = "Edited Test message",
            }
        });
        
        issue = Assert.Single(await db.Issues.ToListAsyncLinqToDB());
        Assert.Equal("Edited Test message", issue.Content);
    }
    
    [Fact]
    public async Task HandleImageMessage_ShouldCreateAndEditIssue_Always()
    {
        using var host = GetTelegramTestHost();

        await host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = DefaultUser,
                Id = 1,
                Photo =
                [
                    new PhotoSize
                    {
                        FileId = "filePreviewId1",
                        FileUniqueId = "filePreviewUniqueId1",
                    },
                    new PhotoSize
                    {
                        FileId = "fileId1",
                        FileUniqueId = "fileUniqueId1",
                    }
                ],
            },
        });

        var scope = host.CreateScope();
        var db = scope.GetDatabaseContext();
        
        var issue = Assert.Single(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Null(issue.Content);
        
        var telegramFiles = await db.TelegramFiles.AsNoTracking().OrderBy(x => x.Id).ToArrayAsyncLinqToDB();
        Assert.Equal(2, telegramFiles.Length);
        
        var previewFile = telegramFiles[0];
        var originalFile = telegramFiles[1];
        Assert.Equal("filePreviewUniqueId1", previewFile.ExternalFileUniqueId);
        Assert.Equal("fileUniqueId1", originalFile.ExternalFileUniqueId);
        
        var attachment = Assert.Single(await db.Attachments.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal(AttachmentType.Image, attachment.Type);
        Assert.Equal(previewFile.FileId, attachment.PreviewFileId);
        Assert.Equal(originalFile.FileId, attachment.FileId);
        
        // Make the image update. Add text and change file.
        await host.SendUpdateAsync(new Update
        {
            EditedMessage = new Message
            {
                From = DefaultUser,
                Id = 1,
                Photo =
                [
                    new PhotoSize
                    {
                        FileId = "filePreviewId2",
                        FileUniqueId = "filePreviewUniqueId2",
                    },
                    new PhotoSize
                    {
                        FileId = "fileId2",
                        FileUniqueId = "fileUniqueId2",
                    }
                ],
                Caption = "Caption",
            }
        });
        
        issue = Assert.Single(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal("Caption", issue.Content);
        
        telegramFiles = await db.TelegramFiles.AsNoTracking().OrderBy(x => x.Id).ToArrayAsyncLinqToDB();
        Assert.Equal(4, telegramFiles.Length);
        
        previewFile = telegramFiles[2];
        originalFile = telegramFiles[3];
        Assert.Equal("filePreviewUniqueId2", previewFile.ExternalFileUniqueId);
        Assert.Equal("fileUniqueId2", originalFile.ExternalFileUniqueId);
        
        attachment = Assert.Single(await db.Attachments.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal(AttachmentType.Image, attachment.Type);
        Assert.Equal(previewFile.FileId, attachment.PreviewFileId);
        Assert.Equal(originalFile.FileId, attachment.FileId);
    }
    
    [Fact]
    public async Task HandleVideoMessage_ShouldCreateAndEditIssue_Always()
    {
        using var host = GetTelegramTestHost();

        await host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = DefaultUser,
                Id = 1,
                Video = new Video
                {
                    FileId = "fileId1",
                    FileUniqueId = "fileUniqueId1",
                    Thumbnail = new PhotoSize
                    {
                        FileId = "filePreviewUniqueId1",
                        FileUniqueId = "filePreviewUniqueId1",
                    }
                },
            },
        });

        var scope = host.CreateScope();
        var db = scope.GetDatabaseContext();
        
        var issue = Assert.Single(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Null(issue.Content);
        
        var telegramFiles = await db.TelegramFiles.AsNoTracking().OrderBy(x => x.Id).ToArrayAsyncLinqToDB();
        Assert.Equal(2, telegramFiles.Length);
        
        var previewFile = telegramFiles[0];
        var originalFile = telegramFiles[1];
        Assert.Equal("filePreviewUniqueId1", previewFile.ExternalFileUniqueId);
        Assert.Equal("fileUniqueId1", originalFile.ExternalFileUniqueId);
        
        var attachment = Assert.Single(await db.Attachments.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal(AttachmentType.Video, attachment.Type);
        Assert.Equal(previewFile.FileId, attachment.PreviewFileId);
        Assert.Equal(originalFile.FileId, attachment.FileId);
        
        // Make the video update. Add text and change file.
        await host.SendUpdateAsync(new Update
        {
            EditedMessage = new Message
            {
                From = DefaultUser,
                Id = 1,
                Video = new Video
                {
                    FileId = "fileId2",
                    FileUniqueId = "fileUniqueId2",
                    Thumbnail = new PhotoSize
                    {
                        FileId = "filePreviewUniqueId2",
                        FileUniqueId = "filePreviewUniqueId2",
                    }
                },
            }
        });
        
        telegramFiles = await db.TelegramFiles.AsNoTracking().OrderBy(x => x.Id).ToArrayAsyncLinqToDB();
        Assert.Equal(4, telegramFiles.Length);
        
        previewFile = telegramFiles[2];
        originalFile = telegramFiles[3];
        Assert.Equal("filePreviewUniqueId2", previewFile.ExternalFileUniqueId);
        Assert.Equal("fileUniqueId2", originalFile.ExternalFileUniqueId);
        
        attachment = Assert.Single(await db.Attachments.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal(AttachmentType.Video, attachment.Type);
        Assert.Equal(previewFile.FileId, attachment.PreviewFileId);
        Assert.Equal(originalFile.FileId, attachment.FileId);
    }
    
    [Fact]
    public async Task HandleAlbumMessage_ShouldCreateAndEditIssue_Always()
    {
        using var host = GetTelegramTestHost();

        // Group is sending as batch of sequential updates
        await host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = DefaultUser,
                Id = 1,
                Video = new Video
                {
                    FileId = "fileId1",
                    FileUniqueId = "fileUniqueId1",
                    Thumbnail = new PhotoSize
                    {
                        FileId = "filePreviewUniqueId1",
                        FileUniqueId = "filePreviewUniqueId1",
                    }
                },
                MediaGroupId = "777",
                Caption = "Caption1",
            },
        });
        
        await host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = DefaultUser,
                Id = 2,
                Photo =
                [
                    new PhotoSize
                    {
                        FileId = "filePreviewId2",
                        FileUniqueId = "filePreviewUniqueId2",
                    },
                    new PhotoSize
                    {
                        FileId = "fileId2",
                        FileUniqueId = "fileUniqueId2",
                    }
                ],
                MediaGroupId = "777",
            },
        });

        var scope = host.CreateScope();
        var db = scope.GetDatabaseContext();
        
        // One group should be merged into one issue
        var issue = Assert.Single(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal("Caption1", issue.Content);
        
        var telegramFiles = await db.TelegramFiles.AsNoTracking().OrderBy(x => x.Id).ToArrayAsyncLinqToDB();
        Assert.Equal(4, telegramFiles.Length);
        
        var previewVideoFile = telegramFiles[0];
        var originalVideoFile = telegramFiles[1];
        var previewImageFile = telegramFiles[2];
        var originalImageFile = telegramFiles[3];
        
        Assert.Equal("filePreviewUniqueId1", previewVideoFile.ExternalFileUniqueId);
        Assert.Equal("fileUniqueId1", originalVideoFile.ExternalFileUniqueId);
        Assert.Equal("filePreviewUniqueId2", previewImageFile.ExternalFileUniqueId);
        Assert.Equal("fileUniqueId2", originalImageFile.ExternalFileUniqueId);
        
        var attachments = await db.Attachments
            .Include(x => x.IssueAttachment)
            .OrderBy(x => x.Id)
            .AsNoTracking()
            .ToListAsyncLinqToDB();
        
        Assert.Equal(2, attachments.Count);
        var videoAttachment = attachments[0];
        var photoAttachment = attachments[1];
        
        Assert.Equal(AttachmentType.Video, videoAttachment.Type);
        Assert.Equal(previewVideoFile.FileId, videoAttachment.PreviewFileId);
        Assert.Equal(originalVideoFile.FileId, videoAttachment.FileId);
        
        Assert.Equal(AttachmentType.Image, photoAttachment.Type);
        Assert.Equal(previewImageFile.FileId, photoAttachment.PreviewFileId);
        Assert.Equal(originalImageFile.FileId, photoAttachment.FileId);
        
        Assert.Equal(issue.Id, photoAttachment.IssueAttachment!.IssueId);
        Assert.Equal(issue.Id, videoAttachment.IssueAttachment!.IssueId);
        
        // Change video with photo
        await host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = DefaultUser,
                Id = 2,
                Video = new Video
                {
                    FileId = "fileId3",
                    FileUniqueId = "fileUniqueId3",
                    Thumbnail = new PhotoSize
                    {
                        FileId = "filePreviewUniqueId3",
                        FileUniqueId = "filePreviewUniqueId3",
                    }
                },
                Caption = "UpdatedCaption",
            },
        });
        
        attachments = await db.Attachments
            .Include(x => x.IssueAttachment)
            .OrderBy(x => x.Id)
            .AsNoTracking()
            .ToListAsyncLinqToDB();
        
        Assert.Equal(2, attachments.Count);
        photoAttachment = attachments[0];
        var photoAttachment2 = attachments[1];
        
        telegramFiles = await db.TelegramFiles.AsNoTracking().OrderBy(x => x.Id).ToArrayAsyncLinqToDB();
        Assert.Equal(6, telegramFiles.Length);
        
        var previewPhoto2File = telegramFiles[4];
        var originalPhoto2File = telegramFiles[5];
        
        Assert.Equal(AttachmentType.Video, photoAttachment2.Type);
        Assert.Equal(previewPhoto2File.FileId, photoAttachment2.PreviewFileId);
        Assert.Equal(originalPhoto2File.FileId, photoAttachment2.FileId);
        
        Assert.Equal(AttachmentType.Video, photoAttachment.Type);
        Assert.Equal(previewVideoFile.FileId, photoAttachment.PreviewFileId);
        Assert.Equal(originalVideoFile.FileId, photoAttachment.FileId);
        
        Assert.Equal(issue.Id, photoAttachment.IssueAttachment!.IssueId);
        Assert.Equal(issue.Id, videoAttachment.IssueAttachment!.IssueId);
    }
    
    [Fact]
    public async Task InlineSearch_ShouldLookupByExactKey_WhenKeyTokenGiven()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var userId = await testScope.CreateUser(x => x.TelegramId = DefaultUser.Id);
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddSpace(userId, "AAA", s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, i => i
                            .WithContent("Hi")))));

        var issueData = organization.GetIssueData(1, 1, 0, 0);

        // Exact key — should return exactly the seeded issue.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery
            {
                From = DefaultUser,
                Query = $"key:{issueData.Key}"
            }
        });

        var foundRequest = host.Requests().Single<AnswerInlineQueryRequest>();
        var foundResult = Assert.Single(foundRequest.Results);
        Assert.Equal(issueData.Key, foundResult.Id);

        // Same shape, wrong number — no such issue exists, should fall through to the
        // generic "no issues found" placeholder rather than erroring.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery
            {
                From = DefaultUser,
                Query = "key:AAA-999"
            }
        });

        var missingRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var missingResult = Assert.Single(missingRequest.Results);
        Assert.Equal("no-issues", missingResult.Id);

        // Malformed key shape (no number) — filter-level validation error, not "no issues".
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery
            {
                From = DefaultUser,
                Query = "key:notakey"
            }
        });

        var invalidRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var invalidResult = Assert.Single(invalidRequest.Results);
        Assert.Equal("key-error", invalidResult.Id);
    }
}