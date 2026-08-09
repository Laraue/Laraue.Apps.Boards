using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices.Resources;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.Boards.TelegramServices;

public interface ITelegramCommandsService
{
    Task HandleStart(
        ReplyData replyData,
        CancellationToken cancellationToken);
}

public class TelegramCommandsService(
    ITelegramBotClient client,
    IOptions<AppOptions> options)
    : ITelegramCommandsService
{
    public Task HandleStart(ReplyData replyData, CancellationToken cancellationToken)
    {
        var appUrl = options.Value.Url;
        
        var markup = new InlineKeyboardMarkup()
            .AddButton(InlineKeyboardButton
                .WithWebApp(
                    $"📋 {string.Format(Phrases.OpenMiniApp)}", new WebAppInfo
                    {
                        Url = appUrl
                    }));
        
        return client.SendMessage(
            replyData.TelegramId,
            string.Format(Phrases.Start, appUrl),
            replyMarkup: markup,
            linkPreviewOptions: new LinkPreviewOptions
            {
                IsDisabled = true,
            },
            cancellationToken: cancellationToken);
    }
}