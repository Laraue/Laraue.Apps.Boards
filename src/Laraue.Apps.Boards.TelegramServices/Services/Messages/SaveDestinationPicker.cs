using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Ai;
using Laraue.Apps.Boards.Services.Billing;
using Laraue.Apps.Boards.TelegramServices.Resources;
using Laraue.Apps.Boards.TelegramServices.Services.GroupChats;
using Laraue.Apps.Boards.TelegramServices.Services.Search;
using Laraue.Telegram.NET.Core.Routing;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

/// <summary>
/// "Where should this go?" for a chat that chooses part of its destination each time (BRD-365): the levels below
/// what the chat fixes - space, epic, status - are picked one by one, and the card is created on the last one.
/// </summary>
public interface ISaveDestinationPicker
{
    /// <summary>
    /// Sends the picker as a reply to <paramref name="replyToExternalMessageId"/>: the saved message in auto mode,
    /// the /save command otherwise (a tap reads the title and /aisave from it).
    /// </summary>
    /// <param name="telegramMessageId">The recorded message to save.</param>
    /// <param name="userId">Whose save it is - the levels list what they can see.</param>
    Task SendPicker(
        long externalChatId,
        int replyToExternalMessageId,
        long telegramMessageId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// A tap in the picker: shows the next level, or creates the card when a status is known.
    /// </summary>
    Task HandlePicked(
        CallbackQuery query,
        Guid userId,
        long telegramMessageId,
        LinkDestinationType destinationType,
        long destinationId,
        CancellationToken cancellationToken);
}

public class SaveDestinationPicker(
    DatabaseContext context,
    ITelegramBotClient client,
    IAccessService accessService,
    ILinkDestinationReader destinationReader,
    ITelegramSaveMessageService saveMessageService,
    ILogger<SaveDestinationPicker> logger)
    : ISaveDestinationPicker
{
    public async Task SendPicker(
        long externalChatId,
        int replyToExternalMessageId,
        long telegramMessageId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var chat = await GetChat(externalChatId, cancellationToken);
        if (chat is null)
            return;

        var level = await BuildLevel(chat, telegramMessageId, chat.DestinationType, chat.DestinationId, userId, cancellationToken);
        if (level is null)
            return;

        await client.SendMessage(
            externalChatId,
            level.Text,
            replyParameters: new ReplyParameters
            {
                MessageId = replyToExternalMessageId,
                AllowSendingWithoutReply = true,
            },
            replyMarkup: level.Markup,
            cancellationToken: cancellationToken);
    }

    public async Task HandlePicked(
        CallbackQuery query,
        Guid userId,
        long telegramMessageId,
        LinkDestinationType destinationType,
        long destinationId,
        CancellationToken cancellationToken)
    {
        var picker = query.Message!;
        var externalChatId = picker.Chat.Id;

        var chat = await GetChat(externalChatId, cancellationToken);
        var message = await context.TelegramMessages
            .Where(x => x.Id == telegramMessageId && x.ExternalChatId == externalChatId)
            .Select(x => new { x.SenderId })
            .FirstOrDefaultAsyncEF(cancellationToken);

        if (chat is null || message is null)
        {
            await AnswerAlert(query, Phrases.LinkItemNotFound, cancellationToken);
            return;
        }

        // In "after /save" mode the picker answers the /save command: its sender owns the pick, and its text carries
        // the title and /aisave. Otherwise - auto mode, or the command was deleted meanwhile - the message's sender.
        var command = picker.ReplyToMessage is { } replied && SaveCommandService.IsSaveCommand(replied.Text)
            ? replied
            : null;

        var isOwner = command is not null
            ? command.From?.Id == query.From.Id
            : message.SenderId == userId;

        if (!isOwner)
        {
            await AnswerAlert(query, Phrases.SavePickerNotYours, cancellationToken);
            return;
        }

        var destination = await destinationReader.Load(destinationType, destinationId, includeDeleted: false, cancellationToken);
        if (destination is null || !IsWithinChatDestination(chat, destination))
        {
            await AnswerAlert(query, Phrases.LinkItemNotFound, cancellationToken);
            return;
        }

        var statusId = await GetStatusToSaveTo(destination, cancellationToken);
        if (statusId is null)
        {
            var level = await BuildLevel(chat, telegramMessageId, destinationType, destinationId, userId, cancellationToken);
            if (level is not null)
            {
                await client.EditMessageText(
                    externalChatId,
                    picker.MessageId,
                    level.Text,
                    replyMarkup: level.Markup,
                    cancellationToken: cancellationToken);
            }

            return;
        }

        await Save(query, userId, telegramMessageId, statusId.Value, command?.Text, cancellationToken);
    }

    private async Task Save(
        CallbackQuery query,
        Guid userId,
        long telegramMessageId,
        long statusId,
        string? commandText,
        CancellationToken cancellationToken)
    {
        var picker = query.Message!;
        var externalChatId = picker.Chat.Id;

        SaveByReplyResult result;
        try
        {
            result = await saveMessageService.SaveToStatus(
                new SaveToStatusRequest
                {
                    ExternalChatId = externalChatId,
                    TelegramMessageId = telegramMessageId,
                    StatusId = statusId,
                    UserId = userId,
                    Title = SaveCommandService.ExtractTitle(commandText),
                    Summarize = SaveCommandService.IsAiSaveCommand(commandText),
                },
                cancellationToken);
        }
        catch (Exception ex) when (ToRefusal(ex) is { } refusal)
        {
            if (ex is AiContentSummarizationException)
                logger.LogWarning(ex, "AI generation failed for chat {ExternalChatId}", externalChatId);

            await AnswerAlert(query, refusal, cancellationToken);
            return;
        }

        var skippedFilesNotice = result.SkippedFiles.Count > 0 ? SkippedFile.FormatNotice(result.SkippedFiles) : null;

        switch (result.Outcome)
        {
            // The chat's reply mode decides, as for any save: in link mode the picker turns into the card itself,
            // with no extra message; in reaction mode it goes away and 👍 goes on the saved message.
            case SaveByReplyOutcome.Saved or SaveByReplyOutcome.AlreadySaved when result.ReplyMode == ReplyMode.Reaction:
                await client.DeleteMessage(externalChatId, picker.MessageId, cancellationToken);
                await client.SetMessageReaction(
                    externalChatId,
                    result.SavedExternalMessageId!.Value,
                    [new ReactionTypeEmoji { Emoji = "👍" }],
                    cancellationToken: cancellationToken);

                if (skippedFilesNotice is not null)
                {
                    await client.SendMessage(
                        externalChatId,
                        skippedFilesNotice,
                        replyParameters: new ReplyParameters
                        {
                            MessageId = result.SavedExternalMessageId.Value,
                            AllowSendingWithoutReply = true,
                        },
                        cancellationToken: cancellationToken);
                }

                break;

            case SaveByReplyOutcome.Saved or SaveByReplyOutcome.AlreadySaved:
                await client.EditMessageText(
                    externalChatId,
                    picker.MessageId,
                    skippedFilesNotice is null
                        ? result.IssuePreviewText!
                        : $"{result.IssuePreviewText}\n\n{SearchTextFormatter.EscapeMarkdownV2(skippedFilesNotice)}",
                    parseMode: ParseMode.MarkdownV2,
                    replyMarkup: new InlineKeyboardMarkup(
                        InlineKeyboardButton.WithUrl(Phrases.OpenIssueButton, result.IssueUrl!)),
                    cancellationToken: cancellationToken);
                break;

            // Nothing a place can fix: the picker says why instead.
            case SaveByReplyOutcome.NothingToSave:
                await client.EditMessageText(externalChatId, picker.MessageId, Phrases.SaveNothingToSave, cancellationToken: cancellationToken);
                break;

            case SaveByReplyOutcome.TitleRequired:
                await client.EditMessageText(externalChatId, picker.MessageId, Phrases.SaveTitleRequired, cancellationToken: cancellationToken);
                break;
        }
    }

    /// <summary>
    /// The next level to choose from, below <paramref name="type"/>/<paramref name="id"/>: spaces the user can see,
    /// a space's epics, or an epic's statuses. Null when there's nothing left to choose.
    /// </summary>
    private async Task<PickerLevel?> BuildLevel(
        PickerChat chat,
        long telegramMessageId,
        LinkDestinationType type,
        long id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var destination = await destinationReader.Load(type, id, includeDeleted: false, cancellationToken);
        if (destination is null)
            return null;

        InlineKeyboardButton PickButton(LinkDestinationType pickedType, long pickedId, string text) =>
            new CallbackRoutePath(TelegramRoutes.SaveDestinationPicked)
                .WithPathParameter(TelegramRoutes.Parameters.MessageId, telegramMessageId.ToString())
                .WithPathParameter(TelegramRoutes.Parameters.DestinationType, ((int)pickedType).ToString())
                .WithPathParameter(TelegramRoutes.Parameters.DestinationId, pickedId.ToString())
                .ToInlineKeyboardButton(text);

        IEnumerable<InlineKeyboardButton[]> rows;
        string prompt;
        PickerPoint? back;

        switch (type)
        {
            case LinkDestinationType.Organization:
                var spaces = await accessService.GetAvailableSpaces(
                    new OrganizationAuthData { UserId = userId, OrganizationId = destination.OrganizationId },
                    x => x
                        .OrderBy(y => y.Id)
                        .Select(y => new { y.Id, y.Name })
                        .ToListAsyncEF(cancellationToken),
                    includeDeleted: false,
                    cancellationToken: cancellationToken);

                rows = spaces.Select(x => new[] { PickButton(LinkDestinationType.Space, x.Id, $"🗂️ {x.Name}") });
                prompt = string.Format(Phrases.LinkChooseSpace, destination.OrganizationName);
                back = null;
                break;

            case LinkDestinationType.Space:
                var epics = await context.ActiveEpics()
                    .Where(x => x.SpaceId == id)
                    .OrderByDescending(x => x.IsDefault)
                    .ThenBy(x => x.Id)
                    .Select(x => new { x.Id, x.Name, x.IsDefault })
                    .ToListAsyncEF(cancellationToken);

                rows = epics.Select(x => new[]
                {
                    PickButton(LinkDestinationType.Epic, x.Id, $"{(x.IsDefault ? "📥" : "📋")} {x.Name}"),
                });
                prompt = string.Format(Phrases.LinkChooseEpic, $"{destination.OrganizationName} → {destination.SpaceName}");
                back = new PickerPoint(LinkDestinationType.Organization, destination.OrganizationId);
                break;

            case LinkDestinationType.Epic:
                var statuses = await context.ActiveStatuses()
                    .Where(x => x.EpicId == id)
                    .OrderBy(x => x.Id)
                    .Select(x => new { x.Id, x.Name })
                    .ToListAsyncEF(cancellationToken);

                rows = statuses.Select(x => new[] { PickButton(LinkDestinationType.Status, x.Id, $"🏷️ {x.Name}") });
                prompt = string.Format(
                    Phrases.LinkChooseStatus,
                    $"{destination.OrganizationName} → {destination.SpaceName} → {destination.EpicName}");
                back = new PickerPoint(LinkDestinationType.Space, destination.SpaceId!.Value);
                break;

            default:
                return null;
        }

        // Back goes up only within what the chat leaves to choose - never above its fixed part.
        if (back is not null && back.Type >= chat.DestinationType)
            rows = rows.AddBackButton(PickRoute(telegramMessageId, back));

        return new PickerLevel(
            $"{Phrases.SavePickerTitle}\n{prompt}",
            new InlineKeyboardMarkup(rows.AddCancelButton()));
    }

    /// <summary>
    /// The status to save to once it's known: a picked status, or the single status of a picked backlog epic (never
    /// shown by name, as in /link). Null when a level is still to be chosen.
    /// </summary>
    private async Task<long?> GetStatusToSaveTo(LinkDestination destination, CancellationToken cancellationToken)
    {
        if (destination.Type == LinkDestinationType.Status)
            return destination.Id;

        if (destination.Type != LinkDestinationType.Epic)
            return null;

        return await context.ActiveStatuses()
            .Where(x => x.EpicId == destination.Id && x.Epic!.IsDefault)
            .OrderBy(x => x.Id)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsyncEF(cancellationToken);
    }

    /// <summary>
    /// A tap can only pick within what the chat fixes: its organization, and below its fixed space or epic. Anything
    /// else is stale or forged callback data.
    /// </summary>
    private static bool IsWithinChatDestination(PickerChat chat, LinkDestination picked)
    {
        if (picked.OrganizationId != chat.OrganizationId || picked.Type < chat.DestinationType)
            return false;

        return chat.DestinationType switch
        {
            LinkDestinationType.Organization => true,
            LinkDestinationType.Space => picked.SpaceId == chat.DestinationId,
            LinkDestinationType.Epic => picked.EpicId == chat.DestinationId,
            _ => false,
        };
    }

    private static string? ToRefusal(Exception ex) => ex switch
    {
        ChatNotLinkedException => Phrases.LinkNotLinked,
        LinkTargetDeletedException => Phrases.LinkTargetDeleted,
        IssueCreationForbiddenException => Phrases.IssueCreationForbidden,
        IssueLimitExceededException => Phrases.IssueLimitExceeded,
        IssueLockedByAgentRunException => Phrases.IssueLockedByAgentRun,
        AiContentSummarizationException => Phrases.AiSummarizationUnavailable,
        InsufficientTokenBalanceException => Phrases.InsufficientTokenBalance,
        _ => null,
    };

    private static CallbackRoutePath PickRoute(long telegramMessageId, PickerPoint point)
    {
        return new CallbackRoutePath(TelegramRoutes.SaveDestinationPicked)
            .WithPathParameter(TelegramRoutes.Parameters.MessageId, telegramMessageId.ToString())
            .WithPathParameter(TelegramRoutes.Parameters.DestinationType, ((int)point.Type).ToString())
            .WithPathParameter(TelegramRoutes.Parameters.DestinationId, point.Id.ToString());
    }

    private Task AnswerAlert(CallbackQuery query, string text, CancellationToken cancellationToken)
    {
        return client.AnswerCallbackQuery(query.Id, text, showAlert: true, cancellationToken: cancellationToken);
    }

    private Task<PickerChat?> GetChat(long externalChatId, CancellationToken cancellationToken)
    {
        return context.LinkedTelegramChats
            .Where(x => x.ExternalChatId == externalChatId && x.UnlinkedAt == null)
            .Select(x => new PickerChat(x.OrganizationId, x.DestinationType, x.DestinationId))
            .FirstOrDefaultAsyncEF(cancellationToken);
    }

    private sealed record PickerChat(long OrganizationId, LinkDestinationType DestinationType, long DestinationId);

    private sealed record PickerPoint(LinkDestinationType Type, long Id);

    private sealed record PickerLevel(string Text, InlineKeyboardMarkup Markup);
}
