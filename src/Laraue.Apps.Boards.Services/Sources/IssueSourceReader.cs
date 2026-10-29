using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services.Members;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.Sources;

/// <summary>
/// Where an issue came from, shared by the hosts that show it (WebApiHost, McpHost). A plain read with <b>no
/// permission checks</b>: the caller checks access to the issue first. The message link is shown to everyone who
/// can read the issue - Telegram itself won't open a private chat's message for non-members.
/// </summary>
public interface IIssueSourceReader
{
    /// <summary>
    /// The Telegram message the issue was created from, null for an issue not created from Telegram.
    /// </summary>
    Task<IssueSourceDto?> GetSource(long issueId, long organizationId, CancellationToken cancellationToken);
}

public class IssueSourceReader(DatabaseContext context, IMemberProfileReader memberProfileReader)
    : IIssueSourceReader
{
    public async Task<IssueSourceDto?> GetSource(long issueId, long organizationId, CancellationToken cancellationToken)
    {
        var message = await context.Issues
            .Where(x => x.Id == issueId)
            .Where(x => x.TelegramMessage != null)
            .Select(x => new
            {
                x.TelegramMessage!.ExternalChatId,
                x.TelegramMessage.ExternalMessageId,
                x.TelegramMessage.SenderId,
                x.TelegramMessage.SentAt,
                ChatTitle = x.TelegramMessage.LinkedTelegramChat!.Title,
                ChatUsername = x.TelegramMessage.LinkedTelegramChat.Username,
            })
            .FirstOrDefaultAsyncEF(cancellationToken);

        if (message is null)
            return null;

        var sender = message.SenderId is { } senderId ? new UserDetails { UserId = senderId } : null;
        if (sender is not null)
            await memberProfileReader.EnrichUsers(
                organizationId,
                [sender],
                cancellationToken);

        var isPrivateChat = TelegramMessageUrlBuilder.IsPrivateChat(message.ExternalChatId);

        return new IssueSourceDto
        {
            IsPrivateChat = isPrivateChat,
            // A private chat's "title" is the owner's username, not something to show as a chat name.
            ChatTitle = isPrivateChat ? null : message.ChatTitle,
            Sender = sender,
            SentAt = message.SentAt,
            // The message's own chat id, not the link's: a message saved before its basic group became a supergroup
            // keeps the old id, and its id means nothing in the supergroup, so it gets no link.
            MessageUrl = TelegramMessageUrlBuilder.Build(
                message.ExternalChatId,
                message.ExternalMessageId,
                message.ChatUsername),
        };
    }
}

/// <summary>
/// The Telegram message an issue was created from.
/// </summary>
public record IssueSourceDto
{
    /// <summary>
    /// True for the private chat with the bot, which has no title and no message links.
    /// </summary>
    public required bool IsPrivateChat { get; init; }

    /// <summary>
    /// The group's title, null for the private chat with the bot or when it isn't known.
    /// </summary>
    public required string? ChatTitle { get; init; }

    /// <summary>
    /// Who sent the message, as shown in the issue's organization; null when unknown.
    /// </summary>
    public required UserDetails? Sender { get; init; }

    /// <summary>
    /// When the message was sent on Telegram (UTC).
    /// </summary>
    public required DateTime? SentAt { get; init; }

    /// <summary>
    /// A t.me link opening the message, null where Telegram has none (basic groups, the private chat with the bot,
    /// messages saved before a basic group became a supergroup).
    /// </summary>
    public required string? MessageUrl { get; init; }
}
