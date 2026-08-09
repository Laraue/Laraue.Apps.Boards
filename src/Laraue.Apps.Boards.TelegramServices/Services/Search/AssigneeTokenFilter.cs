using Laraue.Apps.Boards.DataAccess.Models;
using LinqToDB.EntityFrameworkCore;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Handles "assignee:me" or "assignee:&lt;telegram username&gt;".
/// </summary>
public sealed class AssigneeTokenFilter : IQueryTokenFilter
{
    public string Key => "assignee";

    public async Task<TokenResolution> ResolveAsync(
        FilterContext context,
        IQueryable<Issue> query,
        string value,
        bool isFinalized,
        CancellationToken ct)
    {
        if (string.Equals(value, "me", StringComparison.OrdinalIgnoreCase))
        {
            // "me" is already a complete, unambiguous value — resolve immediately regardless
            // of isFinalized, same reasoning as key: and upd:. There's nothing more useful the
            // user could type to extend "me" into something else, so waiting for a trailing
            // space would just make an already-complete filter silently do nothing until they
            // add one (it'd fall through to a literal, always-failing content search for the
            // text "assignee:me" instead).
            var filtered = query.Where(x => x.AssigneeId == context.RequestContext.UserId);
            return new AppliedResolution(filtered);
        }

        if (!isFinalized)
        {
            // Still typing a username ("assignee:al...") — no live search here to keep this
            // cheap, just offer "me" as a shortcut for the common case. Extend with a real
            // contains-match username lookup if useful.
            var results = new List<InlineQueryResult>
            {
                new InlineQueryResultArticle(
                    "assignee-me",
                    "me",
                    new InputTextMessageContent(SearchTextFormatter.EscapeMarkdownV2("assignee:me "))
                    {
                        ParseMode = ParseMode.MarkdownV2
                    })
                {
                    Description = "assignee:me — issues assigned to you"
                }
            };

            return new SuggestionsResolution(results);
        }

        // Adjust `context.DbContext.Users` to your actual DbSet name if different.
        var matchedUserId = await context.DbContext.Users
            .Where(u => u.TelegramUserName == value)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsyncLinqToDB(ct);

        if (matchedUserId is null)
        {
            return new ErrorResolution(
                "User not found",
                $"No user with username \"{value}\" exists.");
        }

        var filteredByUser = query.Where(x => x.AssigneeId == matchedUserId.Value);
        return new AppliedResolution(filteredByUser);
    }
}