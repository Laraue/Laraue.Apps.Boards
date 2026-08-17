using Laraue.Apps.Boards.TelegramServices.Resources;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public class TelegramMessageService(
    ITelegramBotClient client,
    ITelegramSaveMessageService saveMessageService)
    : ITelegramMessageService
{
    public async Task HandleSaveMessage(
        SaveMessageTelegramRequest request,
        CancellationToken cancellationToken)
    {
        GetOrCreateMessageResult result;
        try
        {
            result = await saveMessageService.Save(request, cancellationToken);
        }
        catch (ChatNotLinkedException)
        {
            await client.SendMessage(
                request.ExternalUserId,
                Phrases.LinkNotLinked,
                cancellationToken: cancellationToken);
            return;
        }

        // If message was created with that request then response,
        // otherwise it is the second, third etc. parts of message
        if (result.Result is Result.MainMessageCreated)
            await SetReaction(request, "👍", cancellationToken);
        else if (result.Result is Result.MainMessageUpdated)
            await SetReaction(request, "❤", cancellationToken);
    }

    private async Task SetReaction(
        SaveMessageTelegramRequest request,
        string? reaction,
        CancellationToken ct)
    {
        await client.SetMessageReaction(
            request.ExternalUserId,
            request.ExternalMessageId,
            reaction is not null
                ? [new ReactionTypeEmoji { Emoji = reaction }]
                : [],
            cancellationToken: ct);
    }
}