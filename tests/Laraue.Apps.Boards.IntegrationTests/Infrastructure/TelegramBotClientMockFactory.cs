using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public class TelegramBotClientMockFactory
{
    public static ITelegramBotClient GetInstance()
    {
        var botClientMock = new Mock<ITelegramBotClient>();

        botClientMock.Setup(x => x.SendRequest(It.IsAny<GetMeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = 1,
                IsBot = true,
                FirstName = "Test Bot",
                Username = "test_bot",
            });

        botClientMock.Setup(x => x.SendRequest(It.IsAny<GetFileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetFileRequest request, CancellationToken _) => new TGFile
            {
                FileId = request.FileId,
                FileUniqueId = request.FileId + "unique",
                FilePath = request.FileId + "/path",
            });
        
        botClientMock.Setup(x => x.SendRequest(It.IsAny<SendPhotoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SendPhotoRequest request, CancellationToken _) =>
            {
                // FileUniqueId is derived from FileId the same way the GetFileRequest mock above
                // does, so a later GetFile(fileId) call resolves to the same FileUniqueId - matching
                // real Telegram semantics (the same file has a stable FileUniqueId regardless of
                // which API call you fetch it through), which CoreFilesService.DownloadToLocalStorage
                // relies on to key the local cache path the same way UpsertDbFile records it in DB.
                var thumbnailFileId = Guid.NewGuid().ToString();
                var originalFileId = Guid.NewGuid().ToString();

                return new Message
                {
                    Photo =
                    [
                        new PhotoSize
                        {
                            Height = 20,
                            Width = 20,
                            FileId = thumbnailFileId,
                            FileUniqueId = thumbnailFileId + "unique",
                            FileSize = 400,
                        },
                        new PhotoSize
                        {
                            Height = 1000,
                            Width = 800,
                            FileId = originalFileId,
                            FileUniqueId = originalFileId + "unique",
                            FileSize = 800000,
                        }
                    ]
                };
            });
        
        return botClientMock.Object;
    }
}