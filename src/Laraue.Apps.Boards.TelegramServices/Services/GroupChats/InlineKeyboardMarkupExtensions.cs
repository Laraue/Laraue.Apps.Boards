using Laraue.Apps.Boards.TelegramServices.Resources;
using Laraue.Telegram.NET.Core.Routing;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.Boards.TelegramServices.Services.GroupChats;

public static class InlineKeyboardMarkupExtensions
{
    public static IEnumerable<InlineKeyboardButton[]> AddCancelButton(
        this IEnumerable<InlineKeyboardButton[]> buttons)
    {
        var button = new CallbackRoutePath(TelegramRoutes.CloseCallbackWindow)
            .ToInlineKeyboardButton(Phrases.LinkCancel);
        
        return buttons.Append([button]);
    }
    
    public static IEnumerable<InlineKeyboardButton[]> AddBackButton(
        this IEnumerable<InlineKeyboardButton[]> buttons,
        CallbackRoutePath callbackPath)
    {
        return buttons.Append([callbackPath.ToInlineKeyboardButton(Phrases.LinkBack)]);
    }
}