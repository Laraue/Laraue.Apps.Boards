using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public static class MessageExtensions
{
    /// <summary>
    /// True when Telegram tagged the message's own text as a bot command (e.g. "/save" or
    /// "/delete"), regardless of who sent it. Commands are never recorded as content, so
    /// replying to one with /save, /info or /delete should say so plainly instead of the more
    /// confusing "not on record"/"no card yet" outcomes that would otherwise show up.
    /// </summary>
    public static bool IsBotCommand(this Message message)
    {
        return message.Entities?.Any(e => e is { Type: MessageEntityType.BotCommand, Offset: 0 }) == true;
    }
}
