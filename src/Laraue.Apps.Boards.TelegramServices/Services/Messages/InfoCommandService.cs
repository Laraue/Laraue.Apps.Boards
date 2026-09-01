using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices.Resources;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public interface IInfoCommandService
{
    /// <summary>
    /// Handles /info: read-only lookup of the card already linked to the replied-to message (or
    /// its whole album), if any. Works regardless of save mode - unlike /save, never creates or
    /// changes anything.
    /// </summary>
    Task HandleInfoCommand(Message message, Guid userId, CancellationToken cancellationToken);
}

public class InfoCommandService(
    ITelegramSaveMessageService saveMessageService,
    IIssueLinkParser issueLinkParser,
    IIssuePreviewBuilder issuePreviewBuilder,
    IAccessService accessService,
    DatabaseContext context,
    ITelegramBotClient client,
    IEphemeralReplySender ephemeralReplySender)
    : IInfoCommandService
{
    public async Task HandleInfoCommand(Message message, Guid userId, CancellationToken cancellationToken)
    {
        var repliedMessage = message.ReplyToMessage;

        // Not every genuine reply carries reply_to_message (Telegram omits it for old enough
        // messages), so this also covers that case, not just "user didn't reply at all".
        if (repliedMessage is null)
        {
            await ephemeralReplySender.SendEphemeralNotice(message, Phrases.InfoNotAReply, cancellationToken);
            return;
        }

        // The bot's own messages are never recorded as TelegramMessage rows, so this would
        // otherwise surface as the more confusing "not on record" outcome below.
        if (repliedMessage.From?.IsBot == true)
        {
            await ephemeralReplySender.SendEphemeralNotice(message, Phrases.InfoMessageFromBot, cancellationToken);
            return;
        }

        // Commands are never recorded as content, so this would otherwise surface as the more
        // confusing "not on record" outcome below.
        if (repliedMessage.IsBotCommand())
        {
            await ephemeralReplySender.SendEphemeralNotice(message, Phrases.ReplyTargetIsCommand, cancellationToken);
            return;
        }

        // A message that mentions issues by link (e.g. someone pasting a Mini App URL, not
        // necessarily a message the bot ever saved as a card) is answered by resolving those
        // links directly, rather than falling back to the "is this message itself a tracked
        // card" lookup below.
        var issueLinks = issueLinkParser.Parse(repliedMessage.Text ?? repliedMessage.Caption);
        if (issueLinks.Count > 0)
        {
            await HandleIssueLinks(message, repliedMessage, issueLinks, userId, cancellationToken);
            return;
        }

        var result = await saveMessageService.GetInfoByReply(
            new InfoByReplyRequest
            {
                ExternalChatId = message.Chat.Id,
                RepliedExternalMessageId = repliedMessage.MessageId,
                UserId = userId,
            },
            cancellationToken);

        switch (result.Outcome)
        {
            case InfoByReplyOutcome.Found:
                await client.SendIssuePreviewReply(
                    message.Chat.Id,
                    repliedMessage.MessageId,
                    result.IssuePreviewText!,
                    result.IssueUrl!,
                    cancellationToken);
                break;

            case InfoByReplyOutcome.NoCardYet:
                await ephemeralReplySender.SendEphemeralNotice(message, Phrases.InfoNoCardYet, cancellationToken);
                break;

            case InfoByReplyOutcome.MessageNotTracked:
                await ephemeralReplySender.SendEphemeralNotice(
                    message,
                    string.Format(Phrases.InfoMessageNotTracked, issueLinkParser.ExampleLink),
                    cancellationToken);
                break;

            case InfoByReplyOutcome.Forbidden:
                await ephemeralReplySender.SendEphemeralNotice(message, Phrases.InfoForbidden, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Resolves each issue link straight to its issue and checks read access, independent of
    /// whether the replied-to message itself is a tracked card.
    /// </summary>
    private async Task HandleIssueLinks(
        Message message,
        Message repliedMessage,
        IReadOnlyList<IssueLinkMatch> issueLinks,
        Guid userId,
        CancellationToken cancellationToken)
    {
        foreach (var link in issueLinks)
        {
            var issueKeyText = $"{link.SpaceKey}-{link.IssueNumber}";

            var issueData = await context.Issues
                .Where(x => x.IssueNumber!.Space!.Key == link.SpaceKey
                    && x.IssueNumber.Number == link.IssueNumber
                    && x.IssueNumber.Space.Organization!.Slug + "-" + x.IssueNumber.Space.Organization.SlugPostfix == link.OrganizationKey)
                .Select(x => new { x.Id, OrganizationId = x.IssueNumber!.Space!.OrganizationId })
                .FirstOrDefaultAsyncEF(cancellationToken);

            // A missing issue and an issue the caller can't read are answered identically -
            // distinguishing them would let a link let someone probe whether a given key exists
            // in an organization they otherwise have no access to.
            if (issueData is null || !await HasReadAccess(issueData.OrganizationId, issueData.Id, userId, cancellationToken))
            {
                await ephemeralReplySender.SendEphemeralNotice(
                    message,
                    string.Format(Phrases.InfoIssueLinkUnavailable, issueKeyText),
                    cancellationToken);
                continue;
            }

            var preview = await issuePreviewBuilder.Build(issueData.Id, cancellationToken);

            await client.SendIssuePreviewReply(
                message.Chat.Id,
                repliedMessage.MessageId,
                preview.Text,
                preview.Url,
                cancellationToken);
        }
    }

    private async Task<bool> HasReadAccess(
        long organizationId,
        long issueId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var authData = new OrganizationAuthData { OrganizationId = organizationId, UserId = userId };
        var accessLevels = await accessService.GetAccessLevelsByIssueId(authData, issueId, cancellationToken);

        return accessLevels?.CanRead == true;
    }
}
