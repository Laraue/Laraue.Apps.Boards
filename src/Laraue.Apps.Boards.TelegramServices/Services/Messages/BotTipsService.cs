using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.TelegramServices.Resources;
using Laraue.Apps.Boards.TelegramServices.Services.GroupChats;
using Laraue.Core.DateTime.Services.Abstractions;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

/// <summary>
/// "💡 Did you know?" tips (BRD-367): new users start with defaults and may never notice the chat settings. When the
/// way someone uses a chat shows a setting would help, the bot tells them once, in their private chat, with a link to
/// the docs. Only for a chat they linked themselves - the settings are theirs to change.
/// </summary>
public interface IBotTipsService
{
    /// <summary>
    /// Called after a card from a Telegram message was created and confirmed. Sends at most one tip; never throws
    /// (except on cancellation), so saving is never affected.
    /// </summary>
    /// <param name="userId">Who saved the card - the tip goes to them.</param>
    /// <param name="externalChatId">The chat the card came from.</param>
    Task AfterCardCreated(Guid userId, long externalChatId, CancellationToken cancellationToken);
}

public sealed class BotTipsService(
    DatabaseContext context,
    ITelegramBotClient botClient,
    IChatSettingsDocs docs,
    IDateTimeProvider dateTimeProvider,
    ILogger<BotTipsService> logger)
    : IBotTipsService
{
    /// <summary>
    /// The user's own cards from a chat that replies with the card link, within <see cref="ReactionsWindow"/>: they
    /// save a lot, so a 👍 would be quieter.
    /// </summary>
    public const int ReactionsCardCount = 10;
    public static readonly TimeSpan ReactionsWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Cards from a group saving every message, by anyone, within <see cref="SaveOnlyViaSaveWindow"/>: a busy group,
    /// where /save would keep the board to what matters.
    /// </summary>
    public const int SaveOnlyViaSaveCardCount = 20;
    public static readonly TimeSpan SaveOnlyViaSaveWindow = TimeSpan.FromDays(7);

    /// <summary>
    /// After any tip, no other for this long.
    /// </summary>
    public static readonly TimeSpan PauseBetweenTips = TimeSpan.FromDays(3);

    private static readonly BotMessageKind[] TipKinds = [BotMessageKind.TipReactions, BotMessageKind.TipSaveOnlyViaSave];

    public async Task AfterCardCreated(Guid userId, long externalChatId, CancellationToken cancellationToken)
    {
        try
        {
            await SendTipIfUseful(userId, externalChatId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Checking tips for user {UserId} failed", userId);
        }
    }

    private async Task SendTipIfUseful(Guid userId, long externalChatId, CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;

        // One query decides most saves: every tip already given, or one given recently.
        var givenTips = await context.UserBotMessages
            .Where(x => x.UserId == userId && TipKinds.Contains(x.Kind))
            .Select(x => new { x.Kind, x.SentAt })
            .ToListAsyncEF(cancellationToken);

        if (givenTips.Count == TipKinds.Length || givenTips.Any(x => x.SentAt > now - PauseBetweenTips))
            return;

        var chat = await context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == externalChatId && x.UnlinkedAt == null && x.OwnerId == userId)
            .Select(x => new { x.Id, x.SaveMode, x.ReplyMode })
            .FirstOrDefaultAsyncEF(cancellationToken);

        // Only the chat's owner can change its settings: a group member who didn't link it gets no tip.
        if (chat is null)
            return;

        var cards = context.Issues.Where(x => x.TelegramMessage!.LinkedTelegramChatId == chat.Id);

        if (givenTips.All(x => x.Kind != BotMessageKind.TipReactions)
            && chat.ReplyMode == ReplyMode.IssueLink
            && await cards.CountAsync(x => x.OwnerId == userId && x.CreatedAt > now - ReactionsWindow, cancellationToken)
                >= ReactionsCardCount)
        {
            await Send(userId, BotMessageKind.TipReactions, Phrases.TipReactions, ChatSettingsDocs.Anchors.HeavyDailyUse, cancellationToken);
            return;
        }

        // A Telegram group's id is negative; a private chat's is the user's own id.
        if (givenTips.All(x => x.Kind != BotMessageKind.TipSaveOnlyViaSave)
            && externalChatId < 0
            && chat.SaveMode == SaveMode.EachMessage
            && await cards.CountAsync(x => x.CreatedAt > now - SaveOnlyViaSaveWindow, cancellationToken)
                >= SaveOnlyViaSaveCardCount)
        {
            await Send(userId, BotMessageKind.TipSaveOnlyViaSave, Phrases.TipSaveOnlyViaSave, ChatSettingsDocs.Anchors.TeamGroup, cancellationToken);
        }
    }

    /// <summary>
    /// Claims the tip for the user first, so two saves at once send it once; then sends it to their private chat. A
    /// refused send (the user never started the bot) is recorded as <see cref="BotMessageStatus.Failed"/>.
    /// </summary>
    private async Task Send(
        Guid userId,
        BotMessageKind kind,
        string text,
        string docsAnchor,
        CancellationToken cancellationToken)
    {
        var telegramId = await context.Users
            .Where(x => x.Id == userId)
            .Select(x => x.TelegramId)
            .FirstOrDefaultAsyncEF(cancellationToken);

        if (telegramId is null)
            return;

        var claimed = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             insert into user_bot_messages (user_id, kind, sent_at, status)
             values ({userId}, {(int)kind}, {dateTimeProvider.UtcNow}, {(int)BotMessageStatus.Sent})
             on conflict do nothing
             """,
            cancellationToken);

        if (claimed == 0)
            return;

        try
        {
            await botClient.SendMessage(
                telegramId.Value,
                text,
                replyMarkup: new InlineKeyboardMarkup(docs.ReadMoreButton(docsAnchor)),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation(ex, "The {Kind} tip to user {UserId} wasn't delivered", kind, userId);

            var error = ex.Message.Length > UserBotMessage.ErrorMaxLength
                ? ex.Message[..UserBotMessage.ErrorMaxLength]
                : ex.Message;

            await context.UserBotMessages
                .Where(x => x.UserId == userId && x.Kind == kind)
                .ExecuteUpdateAsync(
                    u => u
                        .SetProperty(x => x.Status, BotMessageStatus.Failed)
                        .SetProperty(x => x.Error, error),
                    cancellationToken);
        }
    }
}
