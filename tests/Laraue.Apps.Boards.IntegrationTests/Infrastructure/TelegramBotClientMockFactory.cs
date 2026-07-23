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
        
        botClientMock.Setup(x => x.SendRequest(It.IsAny<GetFileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetFileRequest request, CancellationToken _) => new TGFile
            {
                FileId = request.FileId,
                FileUniqueId = request.FileId + "unique",
            });
        
        botClientMock.Setup(x => x.SendRequest(It.IsAny<SendPhotoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SendPhotoRequest request, CancellationToken _) => new Message
            {
                Photo =
                [
                    new PhotoSize
                    {
                        Height = 20,
                        Width = 20,
                        FileId = Guid.NewGuid().ToString(),
                        FileUniqueId = Guid.NewGuid().ToString(),
                        FileSize = 400,
                    },
                    new PhotoSize
                    {
                        Height = 1000,
                        Width = 800,
                        FileId = Guid.NewGuid().ToString(),
                        FileUniqueId = Guid.NewGuid().ToString(),
                        FileSize = 800000,
                    }
                ]
            });
        
        return botClientMock.Object;
    }
}