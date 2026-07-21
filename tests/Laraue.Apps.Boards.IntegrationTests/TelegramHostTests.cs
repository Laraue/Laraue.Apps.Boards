using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
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
                ]
            }
        });

        var scope = host.CreateScope();
        var db = scope.GetDatabaseContext();
        
        var issue = Assert.Single(await db.Issues.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Null(issue.Content);
        
        var telegramFiles = await db.TelegramFiles.AsNoTracking().OrderBy(x => x.Id).ToArrayAsyncLinqToDB();
        Assert.Equal(2, telegramFiles.Length);
        Assert.Equal("filePreviewUniqueId1", telegramFiles[0].ExternalFileUniqueId);
        Assert.Equal("fileUniqueId1", telegramFiles[1].ExternalFileUniqueId);
        
        var attachment = Assert.Single(await db.Attachments.AsNoTracking().ToListAsyncLinqToDB());
        Assert.Equal(AttachmentType.Image, attachment.Type);
    }
    
    
    
    // TODO
    // AddAttachmentToMessage_ShouldEditCard_Always (text -> photo with text)
    // NewImageMessage_ShouldCreateCard_Always
    // EditImageMessage_ShouldEditCard_Always
    // NewVideoMessage_ShouldCreateCard_Always
    // EditVideoMessage_ShouldEditCard_Always
    // NewGroupMessage_ShouldCreateCard_Always (photo with text + video)
    // EditNonFirstAttachment_ShouldEditCard_Always (photo with text + video -> photo with text + photo)
    // EditFirstAttachment_ShouldEditCard_Always (photo with text + video -> video with new text + video)
    // AddTextToNonFirstAttachment_ShouldEditTextCard_Always (photo with text + video -> photo with text + video with text)
    // - the case with possible deleting of first attachment
}