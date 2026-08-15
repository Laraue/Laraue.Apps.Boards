using Laraue.Telegram.NET.Core.Extensions;
using Laraue.Telegram.NET.Core.Utils;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.TelegramServices.Services.GroupChats;

/// <summary>
/// Handles messages in group/supergroup chats: detects commands and turns the
/// replied-to message into a cards, per the linked chat's destination.
/// </summary>
public interface IGroupChatService
{
    /// <summary>
    /// Entry point for every Message/EditedMessage update from a group/supergroup chat that
    /// wasn't otherwise routed (e.g. not a /link or /unlink command).
    /// </summary>
    Task HandleGroupMessage(Message message, CancellationToken cancellationToken);
}

public class GroupChatService(ITelegramBotClient client) : IGroupChatService
{
    public async Task HandleGroupMessage(Message message, CancellationToken cancellationToken)
    {
        var messageBuilder = new TelegramMessageBuilder()
            .Append("Group message received");
        
        await client.SendTextMessageAsync(
            message.Chat.Id,
            messageBuilder,
            replyParameters: new ReplyParameters
            {
                AllowSendingWithoutReply = true,
                MessageId = message.Id
            },
            cancellationToken: cancellationToken);
    }
}