using Laraue.Apps.Boards.TelegramServices.Services.Messages;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.IntegrationTests;

public class EphemeralReplySenderTests
{
    [Fact]
    public async Task SendEphemeralNotice_ShouldFallBackToPublicMessage_WhenBotIsNotGroupAdmin()
    {
        var botClientMock = new Mock<ITelegramBotClient>();

        botClientMock
            .Setup(x => x.SendRequest(
                It.Is<SendMessageRequest>(r => r.ReceiverUserId != null),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException("Bad Request: BOT_NOT_ADMIN", 400));

        botClientMock
            .Setup(x => x.SendRequest(
                It.Is<SendMessageRequest>(r => r.ReceiverUserId == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message());

        var triggeringMessage = new Message
        {
            Chat = new Chat { Id = 555, Type = ChatType.Group },
            From = new User { Id = 42 },
        };

        await botClientMock.Object.SendEphemeralNotice(triggeringMessage, "notice text", CancellationToken.None);

        // The ephemeral attempt (ReceiverUserId set) failed with BOT_NOT_ADMIN, so a second,
        // plain public message must have gone out instead - the command still gets an answer.
        botClientMock.Verify(
            x => x.SendRequest(
                It.Is<SendMessageRequest>(r => r.ReceiverUserId == null && r.Text == "notice text"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
