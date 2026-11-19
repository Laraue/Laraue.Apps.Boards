using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Ai;
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
    /// the /save command otherwise (a tap reads the title from it).
    /// </summary>
    /// <param name="telegramMessageId">The recorded message to save.</param>
    /// <param name="userId">Whose save it is - the levels list what they can see.</param>
    /// <param name="summarize">/aisave: the AI rewrites the text when the card is created. Carried by the picker's
    /// buttons, so it holds even if the command is deleted meanwhile.</param>
    Task SendPicker(
        long externalChatId,
        int replyToExternalMessageId,
        long telegramMessageId,
        Guid userId,
        bool summarize,
        CancellationToken cancellationToken);

    /// <summary>
    /// A tap in the picker: shows the next level, or creates the card when a status is known.
    /// </summary>
    Task HandlePicked(
        CallbackQuery query,
        Guid userId,
        long telegramMessageId,
        bool summarize,
        LinkDestinationType destinationType,
        long destinationId,
        CancellationToken cancellationToken);
}

public class SaveDestinationPicker(
    DatabaseContext context,
    ITelegramBotClient client,
    ILinkDestinationReader destinationReader,
    IDestinationOptionsReader optionsReader,
    ITelegramSaveMessageService saveMessageService,
    ILogger<SaveDestinationPicker> logger)
    : ISaveDestinationPicker
{
    public async Task SendPicker(
        long externalChatId,
        int replyToExternalMessageId,
        long telegramMessageId,
        Guid userId,
        bool summarize,
        CancellationToken cancellationToken)
    {
        var chat = await GetChat(externalChatId, cancellationToken);
        if (chat is null)
            return;

        var save = new PickerSave(telegramMessageId, summarize);
        var level = await BuildLevel(chat, save, chat.DestinationType, chat.DestinationId, userId, cancellationToken);
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
        bool summarize,
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
        // the title (/aisave travels in the buttons). Otherwise it answers the saved message, which is never a
        // command - and if the command was deleted meanwhile, there's no reply at all. Then the message's sender owns
        // the pick, and the title comes from the text.
        var command = picker.ReplyToMessage is { } replied && replied.IsBotCommand()
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

        // Stale or forged callback data: outside the chat's fixed part, or in a space the user can't read (whose name
        // the next level would show).
        var destination = await destinationReader.Load(destinationType, destinationId, includeDeleted: false, cancellationToken);
        if (destination is null
            || !IsWithinChatDestination(chat, destination)
            || !await optionsReader.CanRead(destination, userId, cancellationToken))
        {
            await AnswerAlert(query, Phrases.LinkItemNotFound, cancellationToken);
            return;
        }

        var statusId = await GetStatusToSaveTo(destination, userId, cancellationToken);
        if (statusId is null)
        {
            var level = await BuildLevel(
                chat,
                new PickerSave(telegramMessageId, summarize),
                destinationType,
                destinationId,
                userId,
                cancellationToken);
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

        await Save(query, userId, telegramMessageId, statusId.Value, command?.Text, summarize, cancellationToken);
    }

    private async Task Save(
        CallbackQuery query,
        Guid userId,
        long telegramMessageId,
        long statusId,
        string? commandText,
        bool summarize,
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
                    Title = SaveCommandText.ExtractTitle(commandText),
                    Summarize = summarize,
                },
                cancellationToken);
        }
        catch (Exception ex) when (SaveRefusal.GetMessage(ex) is { } refusal)
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
        PickerSave save,
        LinkDestinationType type,
        long id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var destination = await destinationReader.Load(type, id, includeDeleted: false, cancellationToken);
        if (destination is null)
            return null;

        var (prompt, back) = type switch
        {
            LinkDestinationType.Organization => (
                string.Format(Phrases.LinkChooseSpace, destination.OrganizationName),
                (PickerPoint?)null),
            LinkDestinationType.Space => (
                string.Format(Phrases.LinkChooseEpic, $"{destination.OrganizationName} → {destination.SpaceName}"),
                new PickerPoint(LinkDestinationType.Organization, destination.OrganizationId)),
            LinkDestinationType.Epic => (
                string.Format(
                    Phrases.LinkChooseStatus,
                    $"{destination.OrganizationName} → {destination.SpaceName} → {destination.EpicName}"),
                new PickerPoint(LinkDestinationType.Space, destination.SpaceId!.Value)),
            _ => (null, null),
        };

        if (prompt is null)
            return null;

        var options = await optionsReader.GetOptionsBelow(type, id, destination.OrganizationId, userId, cancellationToken);
        var rows = options.Select(option => new[]
        {
            PickRoute(save, new PickerPoint(option.Type, option.Id)).ToInlineKeyboardButton(option.Label),
        });

        // Back goes up only within what the chat leaves to choose - never above its fixed part.
        if (back is not null && back.Type >= chat.DestinationType)
            rows = rows.AddBackButton(PickRoute(save, back));

        return new PickerLevel(
            $"{Phrases.SavePickerTitle}\n{prompt}",
            new InlineKeyboardMarkup(rows.AddCancelButton()));
    }

    /// <summary>
    /// The status to save to once it's known: a picked status, or the single status of a picked backlog epic (never
    /// shown by name, as in /link). Null when a level is still to be chosen.
    /// </summary>
    private async Task<long?> GetStatusToSaveTo(LinkDestination destination, Guid userId, CancellationToken cancellationToken)
    {
        if (destination.Type == LinkDestinationType.Status)
            return destination.Id;

        return destination.Type == LinkDestinationType.Epic
            ? await optionsReader.GetBacklogStatusId(destination.Id, destination.OrganizationId, userId, cancellationToken)
            : null;
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

    private static CallbackRoutePath PickRoute(PickerSave save, PickerPoint point)
    {
        return new CallbackRoutePath(TelegramRoutes.SaveDestinationPicked)
            .WithPathParameter(TelegramRoutes.Parameters.MessageId, save.TelegramMessageId.ToString())
            // Path parameters are bound from JSON: "true"/"false".
            .WithPathParameter(TelegramRoutes.Parameters.Summarize, save.Summarize ? "true" : "false")
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

    /// <summary>
    /// What every button of one picker carries: the message to save and whether it was /aisave.
    /// </summary>
    private sealed record PickerSave(long TelegramMessageId, bool Summarize);

    private sealed record PickerLevel(string Text, InlineKeyboardMarkup Markup);
}
