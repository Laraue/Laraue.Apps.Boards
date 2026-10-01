using System.Text;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

public static class SearchTextFormatter
{
    private const string ReservedMarkdownV2Characters = "_*[]()~`>#+-=|{}.!\\";

    public static string EscapeMarkdownV2(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (ReservedMarkdownV2Characters.IndexOf(c) >= 0)
                sb.Append('\\');
            sb.Append(c);
        }

        return sb.ToString();
    }

    public static string NormalizeWhitespace(string content)
    {
        var sb = new StringBuilder(content.Length);
        var lastWasSpace = false;

        foreach (var c in content)
        {
            if (c is '\r' or '\n' or '\t' or ' ')
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        return sb.ToString().Trim();
    }
}
