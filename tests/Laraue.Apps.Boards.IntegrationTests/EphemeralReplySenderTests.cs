using Laraue.Apps.Boards.TelegramServices.Services.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.IntegrationTests;

public class EphemeralReplySenderTests
{
    [Theory]
    [InlineData("Bad Request: BOT_NOT_ADMIN")]
    [InlineData("Bad Request: CHAT_NOT_SUPERGROUP")]
    public async Task SendEphemeralNotice_ShouldFallBackToPublicMessage_WhenEphemeralSendFails(
        string apiErrorMessage)
    {
        var botClientMock = new Mock<ITelegramBotClient>();

        botClientMock
            .Setup(x => x.SendRequest(
                It.Is<SendMessageRequest>(r => r.ReceiverUserId != null),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException(apiErrorMessage, 400));

        botClientMock
            .Setup(x => x.SendRequest(
                It.Is<SendMessageRequest>(r => r.ReceiverUserId == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message());

        var sender = new EphemeralReplySender(
            botClientMock.Object,
            NullLogger<EphemeralReplySender>.Instance);

        var triggeringMessage = new Message
        {
            Chat = new Chat { Id = 555, Type = ChatType.Group },
            From = new User { Id = 42 },
        };

        await sender.SendEphemeralNotice(triggeringMessage, "notice text", CancellationToken.None);

        // Whatever the reason Telegram rejected the ephemeral attempt (ReceiverUserId set), a
        // second, plain public message must have gone out instead - the command still gets an
        // answer regardless of the specific failure reason.
        botClientMock.Verify(
            x => x.SendRequest(
                It.Is<SendMessageRequest>(r => r.ReceiverUserId == null && r.Text == "notice text"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
