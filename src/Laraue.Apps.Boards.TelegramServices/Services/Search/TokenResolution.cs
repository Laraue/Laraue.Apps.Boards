using Laraue.Apps.Boards.DataAccess.Models;
using Telegram.Bot.Types.InlineQueryResults;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

public abstract record TokenResolution;

public sealed record AppliedResolution(IQueryable<Issue> Query) : TokenResolution;
 
public sealed record SuggestionsResolution(IReadOnlyList<InlineQueryResult> Results) : TokenResolution;
 
public sealed record ErrorResolution(string Title, string Message) : TokenResolution;