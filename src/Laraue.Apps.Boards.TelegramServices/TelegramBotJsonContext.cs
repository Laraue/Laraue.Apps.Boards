using System.Text.Json.Serialization;
using Telegram.Bot.Types.InlineQueryResults;

namespace Laraue.Apps.Boards.TelegramServices;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(InlineQueryResultArticle))]
public partial class TelegramBotJsonContext : JsonSerializerContext
{
}