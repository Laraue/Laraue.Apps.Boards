using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Billing;
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
        bool notifyOnFailure,
        CancellationToken cancellationToken)
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
        catch (LinkTargetDeletedException)
        {
            if (notifyOnFailure)
            {
                await client.SendMessage(
                    request.ExternalChatId,
                    Phrases.LinkTargetDeleted,
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
        catch (IssueLimitExceededException)
        {
            if (notifyOnFailure)
            {
                await client.SendMessage(
                    request.ExternalChatId,
                    Phrases.IssueLimitExceeded,
                    cancellationToken: cancellationToken);
            }

            return;
        }
        catch (IssueLockedByAgentRunException)
        {
            // An edited message whose card is with the agent: the card keeps the text that was handed off.
            if (notifyOnFailure)
            {
                await client.SendMessage(
                    request.ExternalChatId,
                    Phrases.IssueLockedByAgentRun,
                    cancellationToken: cancellationToken);
            }

            return;
        }

        // Saving is silent, so a document that was the message's only content would be lost unnoticed. The chat
        // is linked by now, so this is said in groups too.
        if (result.SkippedFile is not null)
        {
            await client.SendMessage(
                request.ExternalChatId,
                SkippedFile.FormatNotice([result.SkippedFile]),
                replyParameters: new ReplyParameters
                {
                    MessageId = request.ExternalMessageId,
                    AllowSendingWithoutReply = true,
                },
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
            request.ExternalChatId,
            request.ExternalMessageId,
            reaction is not null
                ? [new ReactionTypeEmoji { Emoji = reaction }]
                : [],
            cancellationToken: ct);
    }
}