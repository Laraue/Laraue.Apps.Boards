using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InlineQueryResults;
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
                Chat = PrivateChat,
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
                Chat = PrivateChat,
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
                Chat = PrivateChat,
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
                Chat = PrivateChat,
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
                Chat = PrivateChat,
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
                Chat = PrivateChat,
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
                Chat = PrivateChat,
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
                Chat = PrivateChat,
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
                Chat = PrivateChat,
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
                Chat = PrivateChat,
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

        // Already-broken key shape (a second "-") — can never become valid by typing more,
        // so it's a filter-level validation error, not "no issues".
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery
            {
                From = DefaultUser,
                Query = "key:AAA-12-34"
            }
        });

        var invalidRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var invalidResult = Assert.Single(invalidRequest.Results);
        Assert.Equal("key-error", invalidResult.Id);
    }
    
    [Fact]
    public async Task InlineSearch_ShouldNotReturnIssuesFromInaccessibleOrganizations_Always()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var userId = await testScope.CreateUser(x => x.TelegramId = 777);
        var otherUserId = await testScope.CreateUser(x => x.TelegramId = 888);

        await testScope.InitializeOrganization(userId, org => org
            .AddUser(otherUserId, builder => builder
                .SetSpaceAccessLevel(1, level => level.CanRead = true))
            .AddSpace(userId, "SEC", s => s
                .AddEpic(userId, e => e
                    .AddIssue(userId, 0, i => i
                        .WithContent("Seen for both users"))))
            .AddIssueToDefaultStatus(userId, i => i.WithContent("Seen for owner only")));

        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery
            {
                From = new User
                {
                    Id = 777,
                    Username = "user1",
                },
                Query = "seen"
            }
        });

        var ownerRequest = host.Requests().Single<AnswerInlineQueryRequest>();
        Assert.Equal(2, ownerRequest.Results.Count());

        // Other user only has read access to the "SEC" space (index 1) via the direct space
        // permission grant above — the default "DEF" space issue (index 0), visible only to the
        // owner, must not leak through even though it also matches the search text.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery
            {
                From = new User
                {
                    Id = 888,
                    Username = "user2",
                },
                Query = "seen"
            }
        });

        var otherUserRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var otherUserResult = Assert.Single(otherUserRequest.Results);
        Assert.Equal("SEC-1", otherUserResult.Id);
    }

    [Fact]
    public async Task InlineSearch_ShouldResolveOrgToken_ForAllCases()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var userId = await testScope.CreateUser(x => x.TelegramId = DefaultUser.Id);
        // OrganizationInitializer always creates the org with Slug "slug" — see
        // OrganizationInitializer.Initialize(), so a single org is enough to exercise every
        // org: outcome without needing multiple distinctly-slugged organizations.
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddIssueToDefaultStatus(userId, i => i.WithContent("OrgIssue")));

        // Empty value — Suggestions: full candidate list (unmarked) plus a hint.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "org:" }
        });

        var emptyRequest = host.Requests().Single<AnswerInlineQueryRequest>();
        var emptyArticles = emptyRequest.Results.Cast<InlineQueryResultArticle>().ToList();
        Assert.Equal(2, emptyArticles.Count); // the one org, unmarked, plus the wildcard hint
        var emptyOrgArticle = emptyArticles[0];
        var emptyHintArticle = emptyArticles[1];
        Assert.Equal($"org-{organization.Id}", emptyOrgArticle.Id);
        Assert.Equal("New Org", emptyOrgArticle.Title);
        Assert.Equal("org-hint", emptyHintArticle.Id);

        // Exact match (case-insensitive) — Applied immediately.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "org:SLUG" }
        });

        var exactRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var exactResult = Assert.Single(exactRequest.Results);
        Assert.Equal("DEF-1", exactResult.Id);

        // Non-exact, last token, prefix matches ≥1 candidate — Suggestions, ✅-marked.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "org:sl" }
        });

        var prefixRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var prefixArticles = prefixRequest.Results.Cast<InlineQueryResultArticle>().ToList();
        Assert.Equal(2, prefixArticles.Count); // same full candidate list, now with the match marked
        var prefixOrgArticle = prefixArticles[0];
        var prefixHintArticle = prefixArticles[1];
        Assert.Equal($"org-{organization.Id}", prefixOrgArticle.Id);
        Assert.Equal("✅ New Org", prefixOrgArticle.Title);
        Assert.Equal("org-hint", prefixHintArticle.Id);

        // Trailing "*" — Applied immediately regardless of match count.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "org:sl*" }
        });

        var wildcardRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var wildcardResult = Assert.Single(wildcardRequest.Results);
        Assert.Equal("DEF-1", wildcardResult.Id);

        // Non-exact, followed by another token — best-effort prefix match, Applied. "deploy"
        // matches no issue content, so this falls through to "no issues" — but the description
        // proves the org prefix was actually applied rather than short-circuiting as a picker/error.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "org:sl deploy" }
        });

        var followedRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var followedArticle = Assert.IsType<InlineQueryResultArticle>(Assert.Single(followedRequest.Results));
        Assert.Equal("no-issues", followedArticle.Id);
        Assert.Contains("\"sl\"", followedArticle.Description);
        Assert.Contains("\"deploy\"", followedArticle.Description);

        // Zero-prefix-match, last token — Error immediately.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "org:zzz" }
        });

        var noMatchRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("org-error", Assert.Single(noMatchRequest.Results).Id);

        // Zero-prefix-match, explicit wildcard — still Error, even though wildcard normally
        // applies unconditionally.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "org:zzz*" }
        });

        var noMatchWildcardRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("org-error", Assert.Single(noMatchWildcardRequest.Results).Id);
    }

    [Fact]
    public async Task InlineSearch_ShouldResolveSpaceToken_ForAllCases()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var userId = await testScope.CreateUser(x => x.TelegramId = DefaultUser.Id);
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddSpace(userId, "AAA", s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, i => i.WithContent("AaaIssue"))))
                .AddSpace(userId, "ABC", s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, i => i.WithContent("AbcIssue"))))
                .AddSpace(userId, "ZZZ", s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, i => i.WithContent("ZzzIssue")))));

        var defSpaceId = organization.GetSpace(0).Id;
        var aaaSpaceId = organization.GetSpace(1).Id;
        var abcSpaceId = organization.GetSpace(2).Id;
        var zzzSpaceId = organization.GetSpace(3).Id;

        // Empty value — Suggestions: full candidate list plus hint.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "space:" }
        });

        var emptyRequest = host.Requests().Single<AnswerInlineQueryRequest>();
        var emptyArticles = emptyRequest.Results.Cast<InlineQueryResultArticle>().ToList();
        Assert.Equal(5, emptyArticles.Count); // DEF, AAA, ABC, ZZZ, plus the wildcard hint
        // The candidate list itself isn't sorted by the app, so address each entry by its own
        // id rather than by position — ToDictionary also fails fast on an unexpected duplicate.
        var emptyById = emptyArticles.ToDictionary(r => r.Id);
        var emptyDefArticle = emptyById[$"space-{defSpaceId}"];
        var emptyAaaArticle = emptyById[$"space-{aaaSpaceId}"];
        var emptyAbcArticle = emptyById[$"space-{abcSpaceId}"];
        var emptyZzzArticle = emptyById[$"space-{zzzSpaceId}"];
        var emptyHintArticle = emptyById["space-hint"];
        Assert.Equal("AdditionalSpace", emptyAaaArticle.Title);
        Assert.Equal("AdditionalSpace", emptyAbcArticle.Title);
        Assert.Equal("Default Space", emptyDefArticle.Title);
        Assert.Equal("AdditionalSpace", emptyZzzArticle.Title);
        Assert.Equal("space-hint", emptyHintArticle.Id);

        // Exact match — Applied immediately.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "space:AAA" }
        });

        var exactRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var exactResult = Assert.Single(exactRequest.Results);
        Assert.Equal("AAA-1", exactResult.Id);

        // Non-exact, last token, prefix matches ≥2 candidates ("AAA" and "ABC") — Suggestions,
        // both ✅-marked.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "space:A" }
        });

        var prefixRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var prefixArticles = prefixRequest.Results.Cast<InlineQueryResultArticle>().ToList();
        Assert.Equal(5, prefixArticles.Count); // same full candidate list, now with matches marked
        var prefixById = prefixArticles.ToDictionary(r => r.Id);
        var prefixDefArticle = prefixById[$"space-{defSpaceId}"];
        var prefixAaaArticle = prefixById[$"space-{aaaSpaceId}"];
        var prefixAbcArticle = prefixById[$"space-{abcSpaceId}"];
        var prefixZzzArticle = prefixById[$"space-{zzzSpaceId}"];
        var prefixHintArticle = prefixById["space-hint"];
        Assert.Equal("Default Space", prefixDefArticle.Title); // doesn't start with "A" — unmarked
        Assert.Equal("✅ AdditionalSpace", prefixAaaArticle.Title);
        Assert.Equal("✅ AdditionalSpace", prefixAbcArticle.Title);
        Assert.Equal("AdditionalSpace", prefixZzzArticle.Title); // doesn't start with "A" — unmarked
        Assert.Equal("space-hint", prefixHintArticle.Id);

        // Trailing "*" — Applied immediately, matching both "AAA" and "ABC".
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "space:A*" }
        });

        var wildcardRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var wildcardIds = wildcardRequest.Results.Select(r => r.Id).OrderBy(id => id).ToList();
        Assert.Equal(["AAA-1", "ABC-1"], wildcardIds);

        // Non-exact, followed by another token — best-effort prefix, Applied.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "space:A deploy" }
        });

        var followedRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var followedArticle = Assert.IsType<InlineQueryResultArticle>(Assert.Single(followedRequest.Results));
        Assert.Equal("no-issues", followedArticle.Id);
        Assert.Contains("\"A\"", followedArticle.Description);
        Assert.Contains("\"deploy\"", followedArticle.Description);

        // Zero-prefix-match, last token — Error.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "space:QQQ" }
        });

        var noMatchRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("space-error", Assert.Single(noMatchRequest.Results).Id);

        // Zero-prefix-match, explicit wildcard — still Error.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "space:QQQ*" }
        });

        var noMatchWildcardRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("space-error", Assert.Single(noMatchWildcardRequest.Results).Id);
    }

    [Fact]
    public async Task InlineSearch_ShouldResolveAssigneeToken_ForAllCases()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var userId = await testScope.CreateUser(x => x.TelegramId = DefaultUser.Id);
        var directUserId = await testScope.CreateUser(x => x.TelegramUserName = "direct_user");
        var noAccessUserId = await testScope.CreateUser(x => x.TelegramUserName = "noaccess_user");
        // A real user whose username happens to be "me" — must never win over the reserved
        // "assignee:me" match for the searching user.
        var meUserId = await testScope.CreateUser(x => x.TelegramUserName = "me");

        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                // Direct per-space grant only (no org-wide CanRead) on the default space (index 0).
                .AddUser(directUserId, b => b.SetSpaceAccessLevel(0, l => l.CanRead = true))
                // Plain org member — no org-wide CanRead, no direct grant on anything.
                .AddUser(noAccessUserId)
                .AddUser(meUserId, b => b.SetGlobalAccessLevel(g => g.CanRead = true))
                .AddSpace(userId, "SEC", s => s
                    .AddEpic(userId, e => e
                        .AddIssue(directUserId, 0, i => i.WithContent("SecIssue"))))
                .AddIssueToDefaultStatus(userId, i => i.WithContent("OwnerIssue"))
                .AddIssue(0, 0, 0, meUserId, i => i.WithContent("MeUserIssue")));

        // Reserved exact match — always resolves to the searching user's own issues (DEF-1,
        // "OwnerIssue"), never falling through to an exact-username lookup that would instead
        // pick up the real "me"-named user's issue (DEF-2, "MeUserIssue"), and never including
        // SEC-1 which is assigned to someone else entirely.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "assignee:me" }
        });

        var meRequest = host.Requests().Single<AnswerInlineQueryRequest>();
        var meResult = Assert.Single(meRequest.Results);
        Assert.Equal("DEF-1", meResult.Id);

        // Empty value — Suggestions: pinned "me" entry, plus candidates scoped to who can read
        // at least one space currently in play (both DEF and SEC, unscoped by default).
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "assignee:" }
        });

        var pickerRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var pickerArticles = pickerRequest.Results.Cast<InlineQueryResultArticle>().ToList();
        // Pinned "me" always comes first, then candidates ordered by username
        // ("direct_user" < "me"), then the hint — this order is guaranteed by
        // AssigneeTokenFilter.BuildPicker itself, not incidental. noaccess_user has no read
        // access anywhere, so its absence is proven by the count alone.
        Assert.Equal(4, pickerArticles.Count);
        var pickerPinnedMeArticle = pickerArticles[0];
        var pickerDirectUserArticle = pickerArticles[1];
        var pickerMeUserArticle = pickerArticles[2];
        var pickerHintArticle = pickerArticles[3];
        Assert.Equal("assignee-me", pickerPinnedMeArticle.Id);
        Assert.Equal("me", pickerPinnedMeArticle.Title);
        Assert.Equal($"assignee-{directUserId}", pickerDirectUserArticle.Id);
        Assert.Equal("direct_user", pickerDirectUserArticle.Title);
        Assert.Equal($"assignee-{meUserId}", pickerMeUserArticle.Id);
        Assert.Equal("me", pickerMeUserArticle.Title);
        Assert.Equal("assignee-hint", pickerHintArticle.Id);

        // Exact username match — Applied immediately, returning directUser's own issue.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "assignee:direct_user" }
        });

        var exactRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("SEC-1", Assert.Single(exactRequest.Results).Id);

        // Non-exact, followed by another token — best-effort prefix, Applied. "blah" doesn't
        // match "SecIssue" content, so this falls through to "no issues" — but the description
        // proves the assignee prefix was actually applied.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "assignee:direct blah" }
        });

        var followedRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var followedArticle = Assert.IsType<InlineQueryResultArticle>(Assert.Single(followedRequest.Results));
        Assert.Equal("no-issues", followedArticle.Id);
        Assert.Contains("direct", followedArticle.Description);
        Assert.Contains("blah", followedArticle.Description);

        // Non-exact, last token, prefix matches ≥1 — Suggestions, ✅-marked.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "assignee:direct" }
        });

        var prefixPickerRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var prefixPickerArticles = prefixPickerRequest.Results.Cast<InlineQueryResultArticle>().ToList();
        Assert.Equal(4, prefixPickerArticles.Count); // same full candidate set, now with the match marked
        var prefixPickerPinnedMeArticle = prefixPickerArticles[0];
        var prefixPickerDirectUserArticle = prefixPickerArticles[1];
        var prefixPickerMeUserArticle = prefixPickerArticles[2];
        var prefixPickerHintArticle = prefixPickerArticles[3];
        Assert.Equal("assignee-me", prefixPickerPinnedMeArticle.Id);
        Assert.Equal("me", prefixPickerPinnedMeArticle.Title); // "me" doesn't start with "direct" — unmarked
        Assert.Equal($"assignee-{directUserId}", prefixPickerDirectUserArticle.Id);
        Assert.Equal("✅ direct_user", prefixPickerDirectUserArticle.Title);
        Assert.Equal($"assignee-{meUserId}", prefixPickerMeUserArticle.Id);
        Assert.Equal("me", prefixPickerMeUserArticle.Title); // unmarked, same reason
        Assert.Equal("assignee-hint", prefixPickerHintArticle.Id);

        // Zero-prefix-match, last token — Error.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "assignee:zzz" }
        });

        var zeroRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("assignee-error", Assert.Single(zeroRequest.Results).Id);

        // Zero-match, explicit wildcard — still Error.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "assignee:zzz*" }
        });

        var zeroWildcardRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("assignee-error", Assert.Single(zeroWildcardRequest.Results).Id);

        // Sequential scoping: space: narrows the effective scope before assignee: runs, so the
        // direct-space-only user (whose grant is on DEF, not SEC) drops out of the picker.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "space:SEC assignee:" }
        });

        var scopedRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var scopedArticles = scopedRequest.Results.Cast<InlineQueryResultArticle>().ToList();
        // Pinned "me", the "me"-named user (org-wide access covers SEC too), and the hint —
        // that's every item; direct_user's grant is on DEF, not SEC, so it's correctly gone.
        Assert.Equal(3, scopedArticles.Count);
        var scopedPinnedMeArticle = scopedArticles[0];
        var scopedMeUserArticle = scopedArticles[1];
        var scopedHintArticle = scopedArticles[2];
        Assert.Equal("assignee-me", scopedPinnedMeArticle.Id);
        Assert.Equal($"assignee-{meUserId}", scopedMeUserArticle.Id);
        Assert.Equal("assignee-hint", scopedHintArticle.Id);

        // Order matters: with assignee: appearing before space: in the query, assignee: resolves
        // (and short-circuits as Suggestions) before space: ever narrows anything — so the
        // direct-space-only user is back in the picker.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "assignee: space:SEC" }
        });

        var unscopedOrderRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        var unscopedOrderArticles = unscopedOrderRequest.Results.Cast<InlineQueryResultArticle>().ToList();
        Assert.Equal(4, unscopedOrderArticles.Count); // same as the fully-unscoped picker above
        var unscopedOrderPinnedMeArticle = unscopedOrderArticles[0];
        var unscopedOrderDirectUserArticle = unscopedOrderArticles[1];
        var unscopedOrderMeUserArticle = unscopedOrderArticles[2];
        var unscopedOrderHintArticle = unscopedOrderArticles[3];
        Assert.Equal("assignee-me", unscopedOrderPinnedMeArticle.Id);
        Assert.Equal($"assignee-{directUserId}", unscopedOrderDirectUserArticle.Id);
        Assert.Equal($"assignee-{meUserId}", unscopedOrderMeUserArticle.Id);
        Assert.Equal("assignee-hint", unscopedOrderHintArticle.Id);
    }

    [Fact]
    public async Task InlineSearch_ShouldResolveKeyToken_ForIncompleteAndEmptyShapes()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var userId = await testScope.CreateUser(x => x.TelegramId = DefaultUser.Id);
        await testScope.InitializeOrganization(userId);

        // Empty value — Preview: a format hint, nothing to pick from.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "key:" }
        });

        var emptyRequest = host.Requests().Single<AnswerInlineQueryRequest>();
        Assert.Equal("key-preview", Assert.Single(emptyRequest.Results).Id);

        // Still a valid prefix (letters only, no dash yet), last token — Preview.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "key:BRD" }
        });

        var prefixRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("key-preview", Assert.Single(prefixRequest.Results).Id);

        // Still valid (dash typed, no digits yet), last token — still Preview.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "key:BRD-" }
        });

        var dashRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("key-preview", Assert.Single(dashRequest.Results).Id);

        // Same incomplete shape, but followed by another token — the user has moved on, so
        // this is a genuine error rather than something still being typed.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "key:BRD- deploy" }
        });

        var followedRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("key-error", Assert.Single(followedRequest.Results).Id);

        // Complete with a single digit — still a valid, complete shape, applies immediately.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "key:BRD-4" }
        });

        var completeRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("no-issues", Assert.Single(completeRequest.Results).Id);
    }

    [Fact]
    public async Task InlineSearch_ShouldResolveUpdToken_ForAllShapeCases()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var userId = await testScope.CreateUser(x => x.TelegramId = DefaultUser.Id);
        await testScope.InitializeOrganization(userId);

        // Empty value — Preview: format hint.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "upd:" }
        });

        var emptyRequest = host.Requests().Single<AnswerInlineQueryRequest>();
        Assert.Equal("upd-preview", Assert.Single(emptyRequest.Results).Id);

        // Still valid (bare operator, no digits yet), last token — Preview.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "upd:>" }
        });

        var operatorRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("upd-preview", Assert.Single(operatorRequest.Results).Id);

        // Still valid (digits typed, no unit yet), last token — Preview.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "upd:7" }
        });

        var digitsRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("upd-preview", Assert.Single(digitsRequest.Results).Id);

        // Same incomplete shape, followed by another token — Error, the user has moved on.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "upd:7 deploy" }
        });

        var followedRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("upd-error", Assert.Single(followedRequest.Results).Id);

        // Already broken (invalid unit) — Error regardless of position.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "upd:7x" }
        });

        var invalidUnitRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("upd-error", Assert.Single(invalidUnitRequest.Results).Id);

        // Already broken (double operator) — Error.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "upd:>>7d" }
        });

        var doubleOpRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("upd-error", Assert.Single(doubleOpRequest.Results).Id);

        // Complete shape — Applied immediately (no such issues exist, hence "no issues").
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "upd:>7d" }
        });

        var completeRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("no-issues", Assert.Single(completeRequest.Results).Id);
    }

    [Fact]
    public async Task InlineSearch_ShouldInvertUpdAgeSemantics_WhenComparingRecentVsStale()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var userId = await testScope.CreateUser(x => x.TelegramId = DefaultUser.Id);
        await testScope.InitializeOrganization(
            userId,
            o => o
                .AddIssueToDefaultStatus(userId, i => i
                    .WithContent("RecentIssue")
                    .WithTimestamp(DateTime.UtcNow.AddDays(-1)))
                .AddIssueToDefaultStatus(userId, i => i
                    .WithContent("StaleIssue")
                    .WithTimestamp(DateTime.UtcNow.AddDays(-10))));

        // "<6d" means "updated less than 6 days ago" (recent) — must return the 1-day-old
        // issue, not the 10-day-old one, despite the stale issue's raw timestamp being smaller.
        // This exact inversion was implemented backwards once during development.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "upd:<6d" }
        });

        var recentRequest = host.Requests().Single<AnswerInlineQueryRequest>();
        Assert.Equal("DEF-1", Assert.Single(recentRequest.Results).Id);

        // ">6d" means "updated more than 6 days ago" (stale) — must return the 10-day-old issue.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "upd:>6d" }
        });

        var staleRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("DEF-2", Assert.Single(staleRequest.Results).Id);
    }

    [Fact]
    public async Task InlineSearch_ShouldShowPlaceholderForKeyLookupWithNoContent_ButExcludeFromFreeTextSearch()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        var userId = await testScope.CreateUser(x => x.TelegramId = DefaultUser.Id);
        await testScope.InitializeOrganization(
            userId,
            o => o.AddIssueToDefaultStatus(userId, i => i.WithContent(string.Empty)));

        // Exact key lookup must still return the issue even though it has no content — shown
        // with a placeholder instead of being skipped.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "key:DEF-1" }
        });

        var keyRequest = host.Requests().Single<AnswerInlineQueryRequest>();
        var keyArticle = Assert.IsType<InlineQueryResultArticle>(Assert.Single(keyRequest.Results));
        Assert.Equal("DEF-1", keyArticle.Id);
        Assert.Contains("no description", keyArticle.Description);

        // Free-text search must NOT surface the same issue — empty content can never
        // ILIKE-match, so it's excluded rather than shown with a placeholder.
        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "Something" }
        });

        var freeTextRequest = host.Requests().Source.OfType<AnswerInlineQueryRequest>().Last();
        Assert.Equal("no-issues", Assert.Single(freeTextRequest.Results).Id);
    }

    [Fact]
    public async Task InlineSearch_ShouldNotCreateIssue_WhenMessageIsViaBot()
    {
        using var host = GetTelegramTestHost();

        // A message sent by the user picking one of our own inline search results — Telegram
        // sets ViaBot for it. HandleAllMessagesMiddleware must treat this as already handled by
        // the search flow, not save it as a new/edited issue.
        await host.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                From = DefaultUser,
                Id = 1,
                Text = "Picked from inline search",
                ViaBot = new User { Id = 999, Username = "some_bot" },
                Chat = PrivateChat,
            }
        });

        var scope = host.CreateScope();
        var db = scope.GetDatabaseContext();

        Assert.Empty(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
    }

    [Fact]
    public async Task InlineSearch_ShouldReturnNoAccessibleSpaces_WhenUserHasNoOrganizationRelationship()
    {
        using var host = GetTelegramTestHost();
        var testScope = host.CreateTestScope();

        await testScope.CreateUser(x => x.TelegramId = DefaultUser.Id);

        await host.SendUpdateAsync(new Update
        {
            InlineQuery = new InlineQuery { From = DefaultUser, Query = "anything" }
        });

        var request = host.Requests().Single<AnswerInlineQueryRequest>();
        Assert.True(request.IsPersonal);
        Assert.Equal(0, request.CacheTime);
        Assert.Equal("no-spaces", Assert.Single(request.Results).Id);
    }
}