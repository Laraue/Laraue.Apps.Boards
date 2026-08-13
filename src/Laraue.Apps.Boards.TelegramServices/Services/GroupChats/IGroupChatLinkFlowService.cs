using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.TelegramServices.Resources;
using Laraue.Telegram.NET.Core.Routing;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.Boards.TelegramServices.Services.GroupChats;

/// <summary>
/// Drives the Telegram side (messages, inline keyboards) of the /link flow,
/// on top of the data operations in <see cref="IGroupChatService"/>.
/// </summary>
public interface IGroupChatLinkFlowService
{
    Task HandleLinkCommand(Message message, Guid userId, CancellationToken cancellationToken);

    Task HandleOrgSelected(CallbackQuery callbackQuery, Guid userId, long orgId, CancellationToken cancellationToken);

    /// <summary>
    /// A space was picked - shows the "use backlog" vs "choose epic &amp; status" menu.
    /// </summary>
    Task HandleSpaceSelected(
        CallbackQuery callbackQuery,
        Guid userId,
        long orgId,
        long spaceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finalizes the link at "backlog" level (no epic/status narrowing).
    /// </summary>
    Task HandleUseBacklog(
        CallbackQuery callbackQuery,
        Guid userId,
        long orgId,
        long spaceId,
        CancellationToken cancellationToken);

    Task HandleChooseEpicAndStatus(
        CallbackQuery callbackQuery,
        Guid userId,
        long orgId,
        long spaceId,
        CancellationToken cancellationToken);

    Task HandleEpicSelected(
        CallbackQuery callbackQuery,
        Guid userId,
        long orgId,
        long spaceId,
        long epicId,
        CancellationToken cancellationToken);

    Task HandleStatusSelected(
        CallbackQuery callbackQuery,
        Guid userId,
        long orgId,
        long spaceId,
        long epicId,
        long statusId,
        CancellationToken cancellationToken);

    Task HandleBack(CallbackQuery callbackQuery, Guid userId, CancellationToken cancellationToken);

    Task HandleChangeLink(CallbackQuery callbackQuery, Guid userId, CancellationToken cancellationToken);

    Task HandleUnlinkCallback(CallbackQuery callbackQuery, CancellationToken cancellationToken);

    Task HandleUnlinkCommand(Message message, CancellationToken cancellationToken);
}

public class GroupChatLinkFlowService(
    IGroupChatService groupChatService,
    ITelegramChatAdminService chatAdminService,
    ITelegramBotClient client)
    : IGroupChatLinkFlowService
{
    public async Task HandleLinkCommand(Message message, Guid userId, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;

        if (!await chatAdminService.IsAdmin(chatId, message.From!.Id, cancellationToken))
        {
            await client.SendMessage(chatId, Phrases.LinkOnlyAdmins, cancellationToken: cancellationToken);
            return;
        }

        var existingLink = await groupChatService.GetLink(chatId, cancellationToken);
        if (IsLinked(existingLink))
        {
            await SendAlreadyLinkedMenu(chatId, existingLink!, cancellationToken);
            return;
        }

        await SendOrganizationPicker(chatId, userId, cancellationToken);
    }

    public async Task HandleChangeLink(CallbackQuery callbackQuery, Guid userId, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message!.Chat.Id;

        if (!await chatAdminService.IsAdmin(chatId, callbackQuery.From.Id, cancellationToken))
        {
            await client.AnswerCallbackQuery(callbackQuery.Id, Phrases.LinkOnlyAdmins, cancellationToken: cancellationToken);
            return;
        }

        await SendOrganizationPicker(chatId, userId, cancellationToken, editMessageId: callbackQuery.Message.MessageId);
    }

    public async Task HandleUnlinkCallback(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message!.Chat.Id;

        if (!await chatAdminService.IsAdmin(chatId, callbackQuery.From.Id, cancellationToken))
        {
            await client.AnswerCallbackQuery(callbackQuery.Id, Phrases.LinkOnlyAdmins, cancellationToken: cancellationToken);
            return;
        }

        await groupChatService.Unlink(chatId, cancellationToken);

        await client.EditMessageText(
            chatId,
            callbackQuery.Message.MessageId,
            Phrases.LinkUnlinked,
            cancellationToken: cancellationToken);
    }

    public async Task HandleUnlinkCommand(Message message, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;

        if (!await chatAdminService.IsAdmin(chatId, message.From!.Id, cancellationToken))
        {
            await client.SendMessage(chatId, Phrases.LinkOnlyAdmins, cancellationToken: cancellationToken);
            return;
        }

        await groupChatService.Unlink(chatId, cancellationToken);

        await client.SendMessage(chatId, Phrases.LinkUnlinked, cancellationToken: cancellationToken);
    }

    public async Task HandleOrgSelected(
        CallbackQuery callbackQuery,
        Guid userId,
        long orgId,
        CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message!.Chat.Id;

        if (!await IsAllowedToLink(chatId, callbackQuery, userId, orgId, cancellationToken))
            return;

        var organizationName = await groupChatService.GetOrganizationName(orgId, cancellationToken);
        var spaces = await groupChatService.GetSpaces(orgId, cancellationToken);

        var buttons = spaces
            .Select(space => new[]
            {
                new CallbackRoutePath($"link/space/{space.Id}")
                    .WithQueryParameter("orgId", orgId)
                    .ToInlineKeyboardButton($"📋 {space.Name}")
            })
            .Append([new CallbackRoutePath("link/back").ToInlineKeyboardButton(Phrases.LinkBack)]);

        await client.EditMessageText(
            chatId,
            callbackQuery.Message.MessageId,
            string.Format(Phrases.LinkChooseSpace, organizationName),
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }

    public async Task HandleSpaceSelected(
        CallbackQuery callbackQuery,
        Guid userId,
        long orgId,
        long spaceId,
        CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message!.Chat.Id;

        if (!await IsAllowedToLink(chatId, callbackQuery, userId, orgId, cancellationToken))
            return;

        var spaceName = await groupChatService.GetSpaceName(spaceId, cancellationToken);

        var buttons = new[]
        {
            new[]
            {
                new CallbackRoutePath($"link/backlog/{spaceId}")
                    .WithQueryParameter("orgId", orgId)
                    .ToInlineKeyboardButton(Phrases.LinkUseBacklog)
            },
            new[]
            {
                new CallbackRoutePath($"link/epics/{spaceId}")
                    .WithQueryParameter("orgId", orgId)
                    .ToInlineKeyboardButton(Phrases.LinkChooseEpicAndStatus)
            },
            new[] { new CallbackRoutePath("link/back").ToInlineKeyboardButton(Phrases.LinkBack) },
        };

        await client.EditMessageText(
            chatId,
            callbackQuery.Message.MessageId,
            string.Format(Phrases.LinkChooseDestinationDepth, spaceName),
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }

    public async Task HandleUseBacklog(
        CallbackQuery callbackQuery,
        Guid userId,
        long orgId,
        long spaceId,
        CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message!.Chat.Id;

        if (!await IsAllowedToLink(chatId, callbackQuery, userId, orgId, cancellationToken))
            return;

        var destination = await groupChatService.LinkToSpace(
            chatId,
            callbackQuery.Message.Chat.Title,
            orgId,
            spaceId,
            userId,
            cancellationToken);

        await SendLinkConfirmed(chatId, callbackQuery.Message.MessageId, destination, cancellationToken);
    }

    public async Task HandleChooseEpicAndStatus(
        CallbackQuery callbackQuery,
        Guid userId,
        long orgId,
        long spaceId,
        CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message!.Chat.Id;

        if (!await IsAllowedToLink(chatId, callbackQuery, userId, orgId, cancellationToken))
            return;

        var epics = await groupChatService.GetEpics(spaceId, cancellationToken);

        var buttons = epics
            .Select(epic => new[]
            {
                new CallbackRoutePath($"link/epic/{epic.Id}")
                    .WithQueryParameter("spaceId", spaceId)
                    .WithQueryParameter("orgId", orgId)
                    .ToInlineKeyboardButton(epic.Name)
            })
            .Append([
                new CallbackRoutePath($"link/space/{spaceId}")
                    .WithQueryParameter("orgId", orgId)
                    .ToInlineKeyboardButton(Phrases.LinkBack)
            ]);

        await client.EditMessageText(
            chatId,
            callbackQuery.Message.MessageId,
            Phrases.LinkChooseEpic,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }

    public async Task HandleEpicSelected(
        CallbackQuery callbackQuery,
        Guid userId,
        long orgId,
        long spaceId,
        long epicId,
        CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message!.Chat.Id;

        if (!await IsAllowedToLink(chatId, callbackQuery, userId, orgId, cancellationToken))
            return;

        var epicName = await groupChatService.GetEpicName(epicId, cancellationToken);
        var statuses = await groupChatService.GetStatuses(epicId, cancellationToken);

        var buttons = statuses
            .Select(status => new[]
            {
                new CallbackRoutePath($"link/status/{status.Id}")
                    .WithQueryParameter("epicId", epicId)
                    .WithQueryParameter("spaceId", spaceId)
                    .WithQueryParameter("orgId", orgId)
                    .ToInlineKeyboardButton(status.Name)
            })
            .Append([
                new CallbackRoutePath($"link/epics/{spaceId}")
                    .WithQueryParameter("orgId", orgId)
                    .ToInlineKeyboardButton(Phrases.LinkBack)
            ]);

        await client.EditMessageText(
            chatId,
            callbackQuery.Message.MessageId,
            string.Format(Phrases.LinkChooseStatus, epicName),
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }

    public async Task HandleStatusSelected(
        CallbackQuery callbackQuery,
        Guid userId,
        long orgId,
        long spaceId,
        long epicId,
        long statusId,
        CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message!.Chat.Id;

        if (!await IsAllowedToLink(chatId, callbackQuery, userId, orgId, cancellationToken))
            return;

        var destination = await groupChatService.LinkToStatus(
            chatId,
            callbackQuery.Message.Chat.Title,
            orgId,
            spaceId,
            epicId,
            statusId,
            userId,
            cancellationToken);

        await SendLinkConfirmed(chatId, callbackQuery.Message.MessageId, destination, cancellationToken);
    }

    private async Task SendLinkConfirmed(
        ChatId chatId,
        int messageId,
        LinkedChatDestination destination,
        CancellationToken cancellationToken)
    {
        var bot = await client.GetMe(cancellationToken);

        await client.EditMessageText(
            chatId,
            messageId,
            string.Format(Phrases.LinkConfirmed, destination.BuildPath(), bot.Username),
            cancellationToken: cancellationToken);
    }

    public async Task HandleBack(CallbackQuery callbackQuery, Guid userId, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message!.Chat.Id;

        if (!await chatAdminService.IsAdmin(chatId, callbackQuery.From.Id, cancellationToken))
        {
            await client.AnswerCallbackQuery(callbackQuery.Id, Phrases.LinkOnlyAdmins, cancellationToken: cancellationToken);
            return;
        }

        await SendOrganizationPicker(chatId, userId, cancellationToken, editMessageId: callbackQuery.Message.MessageId);
    }

    /// <summary>
    /// Every callback route independently re-validates the tapping user is still a chat admin
    /// and the organization is still eligible - any group member can tap a button on a message
    /// the bot posted, not just the original invoker.
    /// </summary>
    private async Task<bool> IsAllowedToLink(
        ChatId chatId,
        CallbackQuery callbackQuery,
        Guid userId,
        long orgId,
        CancellationToken cancellationToken)
    {
        if (!await chatAdminService.IsAdmin(chatId, callbackQuery.From.Id, cancellationToken))
        {
            await client.AnswerCallbackQuery(callbackQuery.Id, Phrases.LinkOnlyAdmins, cancellationToken: cancellationToken);
            return false;
        }

        if (!await groupChatService.CanLinkToOrganization(userId, orgId, cancellationToken))
        {
            await client.AnswerCallbackQuery(callbackQuery.Id, Phrases.LinkOnlyAdmins, cancellationToken: cancellationToken);
            return false;
        }

        return true;
    }

    private async Task SendOrganizationPicker(
        ChatId chatId,
        Guid userId,
        CancellationToken cancellationToken,
        int? editMessageId = null)
    {
        var organizations = await groupChatService.GetLinkableOrganizations(userId, cancellationToken);

        string text;
        InlineKeyboardMarkup? markup;

        if (organizations.Count == 0)
        {
            var bot = await client.GetMe(cancellationToken);
            text = string.Format(Phrases.LinkNoOrganizations, bot.Username);
            markup = null;
        }
        else
        {
            text = Phrases.LinkChooseOrganization;
            markup = new InlineKeyboardMarkup(organizations
                .Select(org => new[]
                {
                    new CallbackRoutePath($"link/org/{org.Id}")
                        .ToInlineKeyboardButton($"🏢 {org.Name}")
                }));
        }

        if (editMessageId is not null)
        {
            await client.EditMessageText(
                chatId,
                editMessageId.Value,
                text,
                replyMarkup: markup,
                cancellationToken: cancellationToken);

            return;
        }

        await client.SendMessage(
            chatId,
            text,
            replyMarkup: markup,
            cancellationToken: cancellationToken);
    }

    private async Task SendAlreadyLinkedMenu(
        ChatId chatId,
        LinkedTelegramChat link,
        CancellationToken cancellationToken)
    {
        var markup = new InlineKeyboardMarkup(new[]
        {
            new[] { new CallbackRoutePath("link/change").ToInlineKeyboardButton(Phrases.LinkChangeLink) },
            new[] { new CallbackRoutePath("link/unlink").ToInlineKeyboardButton(Phrases.LinkUnlinkButton) },
        });

        var destination = new LinkedChatDestination(
            link.Organization!.Name,
            link.Space!.Name,
            link.Epic?.Name,
            link.Status?.Name);

        await client.SendMessage(
            chatId,
            string.Format(Phrases.LinkAlreadyLinked, destination.BuildPath()),
            replyMarkup: markup,
            cancellationToken: cancellationToken);
    }

    private static bool IsLinked(LinkedTelegramChat? link)
        => link is { OrganizationId: not null, SpaceId: not null };
}
