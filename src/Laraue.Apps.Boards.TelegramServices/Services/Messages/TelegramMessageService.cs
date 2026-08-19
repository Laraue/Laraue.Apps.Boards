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
        CancellationToken cancellationToken,
        bool notifyOnFailure = true)
    {
        GetOrCreateMessageResult result;
        try
        {
            result = await saveMessageService.Save(request, cancellationToken);
        }
        catch (ChatNotLinkedException)
        {
            if (notifyOnFailure)
            {
                await client.SendMessage(
                    request.ExternalChatId,
                    Phrases.LinkNotLinked,
                    cancellationToken: cancellationToken);
            }

            return;
        }
        catch (IssueCreationForbiddenException)
        {
            if (notifyOnFailure)
            {
                await client.SendMessage(
                    request.ExternalChatId,
                    Phrases.IssueCreationForbidden,
                    cancellationToken: cancellationToken);
            }

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
            request.ExternalChatId,
            request.ExternalMessageId,
            reaction is not null
                ? [new ReactionTypeEmoji { Emoji = reaction }]
                : [],
            cancellationToken: ct);
    }
}