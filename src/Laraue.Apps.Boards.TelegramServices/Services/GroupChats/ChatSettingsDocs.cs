using Laraue.Apps.Boards.TelegramServices.Resources;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.Boards.TelegramServices.Services.GroupChats;

/// <summary>
/// Links to the web app's "Telegram chat settings" docs page (BRD-366) from the /link steps, in the user's
/// language: the page's path is a phrase (<c>/documentation/...</c> or <c>/ru/documentation/...</c>), resolved
/// per update like every other text.
/// </summary>
public interface IChatSettingsDocs
{
    /// <summary>
    /// "📖 Which to choose?" opening the section at <paramref name="anchor"/> - one of <see cref="ChatSettingsDocs.Anchors"/>.
    /// </summary>
    InlineKeyboardButton WhichToChooseButton(string anchor);

    /// <summary>
    /// "📖 Read more" opening the section at <paramref name="anchor"/>, e.g. under a tip.
    /// </summary>
    InlineKeyboardButton ReadMoreButton(string anchor);
}

public class ChatSettingsDocs(IOptions<AppOptions> options) : IChatSettingsDocs
{
    /// <summary>
    /// The page's sections: explicit heading ids, the same in both languages.
    /// </summary>
    public static class Anchors
    {
        public const string SaveMode = "save-mode";
        public const string Destination = "destination";
        public const string ReplyMode = "reply-mode";
        public const string UseCases = "use-cases";
        public const string TeamGroup = "team-group";
        public const string HeavyDailyUse = "heavy-daily-use";
    }

    public InlineKeyboardButton WhichToChooseButton(string anchor)
    {
        return InlineKeyboardButton.WithUrl(Phrases.DocsWhichToChooseButton, Url(anchor));
    }

    public InlineKeyboardButton ReadMoreButton(string anchor)
    {
        return InlineKeyboardButton.WithUrl(Phrases.DocsReadMoreButton, Url(anchor));
    }

    private string Url(string anchor)
    {
        return $"{options.Value.Url.TrimEnd('/')}{Phrases.DocsChatSettingsPath}#{anchor}";
    }
}
