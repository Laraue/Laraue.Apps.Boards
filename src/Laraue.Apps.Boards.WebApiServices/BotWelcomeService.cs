using System.Globalization;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.Boards.WebApiServices;

/// <summary>
/// The bot's welcome to a user who signed up with Telegram on the web or in the Mini App (BRD-354): they never
/// wrote to the bot, so nothing told them that a message sent to it becomes a card. Users who sign up by writing to
/// the bot get the <c>/start</c> reply instead. Sent once per user (<see cref="UserBotMessage"/>); a user who
/// didn't allow the bot to write can't be reached, which is recorded as <see cref="BotMessageStatus.Failed"/>.
/// </summary>
public interface IBotWelcomeService
{
    /// <summary>
    /// Sends the welcome unless this user already got one. Never throws (except on cancellation): a failed
    /// send is recorded and logged, the sign-in goes on.
    /// </summary>
    Task SendWelcome(Guid userId, long telegramId, string? languageCode, CancellationToken cancellationToken);
}

public sealed class BotWelcomeService(
    DatabaseContext context,
    ITelegramBotClient botClient,
    IOptions<WebAppOptions> webAppOptions,
    IDateTimeProvider dateTimeProvider,
    ILogger<BotWelcomeService> logger)
    : IBotWelcomeService
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru");

    public async Task SendWelcome(
        Guid userId,
        long telegramId,
        string? languageCode,
        CancellationToken cancellationToken)
    {
        // Claim the message first: of two concurrent sign-ins of the same user only one inserts the row and sends.
        var claimed = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             insert into user_bot_messages (user_id, kind, sent_at, status)
             values ({userId}, {(int)BotMessageKind.Welcome}, {dateTimeProvider.UtcNow}, {(int)BotMessageStatus.Sent})
             on conflict do nothing
             """,
            cancellationToken);

        if (claimed == 0)
            return;

        // The sign-in language decides the text; the bot itself answers in the language of the Telegram app too.
        var culture = languageCode?.StartsWith("ru", StringComparison.OrdinalIgnoreCase) == true
            ? Russian
            : CultureInfo.InvariantCulture;

        try
        {
            await botClient.SendMessage(
                telegramId,
                BotMessages.ResourceManager.GetString(nameof(BotMessages.Welcome), culture)!,
                replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithWebApp(
                    BotMessages.ResourceManager.GetString(nameof(BotMessages.OpenBoard), culture)!,
                    new WebAppInfo { Url = $"{webAppOptions.Value.Url.TrimEnd('/')}/login" })),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Expected when the user didn't allow the bot to write to them at sign-in.
            logger.LogInformation(ex, "The welcome message to user {UserId} wasn't delivered", userId);

            var error = ex.Message.Length > UserBotMessage.ErrorMaxLength
                ? ex.Message[..UserBotMessage.ErrorMaxLength]
                : ex.Message;

            await context.UserBotMessages
                .Where(x => x.UserId == userId && x.Kind == BotMessageKind.Welcome)
                .ExecuteUpdateAsync(
                    u => u
                        .SetProperty(x => x.Status, BotMessageStatus.Failed)
                        .SetProperty(x => x.Error, error),
                    cancellationToken);
        }
    }
}
