namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public interface ITelegramMessageService
{
    /// <summary>
    /// Saves an incoming message per its chat's <see cref="Laraue.Apps.Boards.DataAccess.Models.LinkedTelegramChat"/>.
    /// </summary>
    /// <param name="notifyOnFailure">
    /// Whether to reply when the chat has no active link, or the user isn't allowed to create
    /// cards there. Private chats are always linked from registration (and owned by the user
    /// posting in them), so seeing either notice means something is actually wrong - worth
    /// surfacing. Most group chats the bot is added to are never linked at all, and members
    /// without create access post there routinely, so replying to every such message would just
    /// be spam; pass false there to fail silently.
    /// </param>
    Task HandleSaveMessage(
        SaveMessageTelegramRequest request,
        CancellationToken cancellationToken,
        bool notifyOnFailure = true);
}