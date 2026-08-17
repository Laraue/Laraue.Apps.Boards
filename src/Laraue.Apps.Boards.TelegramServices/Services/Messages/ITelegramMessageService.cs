namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public interface ITelegramMessageService
{
    /// <summary>
    /// Saves an incoming message per its chat's <see cref="Laraue.Apps.Boards.DataAccess.Models.LinkedTelegramChat"/>.
    /// </summary>
    Task HandleSaveMessage(
        SaveMessageTelegramRequest request,
        CancellationToken cancellationToken,
        bool notifyWhenNotLinked = true);
}