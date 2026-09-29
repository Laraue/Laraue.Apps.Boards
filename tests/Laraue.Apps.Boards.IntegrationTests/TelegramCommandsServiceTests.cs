using Laraue.Apps.Boards.TelegramServices;
using Microsoft.Extensions.Options;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.Boards.IntegrationTests;

public class TelegramCommandsServiceTests
{
    private const string AppUrl = "https://boards.example.com";

    [Fact]
    public async Task HandleStart_ShouldOpenMiniAppAtLoginPage_WhenBotStarted()
    {
        var botClientMock = new Mock<ITelegramBotClient>();

        botClientMock
            .Setup(x => x.SendRequest(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message());

        var service = new TelegramCommandsService(
            botClientMock.Object,
            Options.Create(new AppOptions
            {
                Url = AppUrl,
                Icons = new IconsUrls
                {
                    Issue = $"{AppUrl}/icons/issue.png",
                    Organization = $"{AppUrl}/icons/organization.png",
                    User = $"{AppUrl}/icons/user.png",
                    Hint = $"{AppUrl}/icons/hint.png",
                    Space = $"{AppUrl}/icons/space.png",
                },
            }));

        await service.HandleStart(
            new ReplyData(Guid.NewGuid(), 555, 1),
            CancellationToken.None);

        // The site's root is the landing page, so the Mini App has to start at the login page.
        botClientMock.Verify(
            x => x.SendRequest(
                It.Is<SendMessageRequest>(r => HasSingleWebAppButton(r, $"{AppUrl}/login")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static bool HasSingleWebAppButton(SendMessageRequest request, string url)
    {
        var buttons = (request.ReplyMarkup as InlineKeyboardMarkup)?
            .InlineKeyboard
            .SelectMany(row => row)
            .ToList();

        return buttons is [{ WebApp: { } webApp }] && webApp.Url == url;
    }
}
