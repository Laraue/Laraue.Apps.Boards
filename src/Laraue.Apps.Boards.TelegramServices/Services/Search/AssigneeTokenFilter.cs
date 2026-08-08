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
        if (!isFinalized)
        {
            // No live username search here to keep this cheap — just a shortcut for the common case.
            // Extend with a real username lookup (e.g. contains-match, Take(10)) if useful.
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

        Guid assigneeId;

        if (string.Equals(value, "me", StringComparison.OrdinalIgnoreCase))
        {
            assigneeId = context.RequestContext.UserId;
        }
        else
        {
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

            assigneeId = matchedUserId.Value;
        }

        var filtered = query.Where(x => x.AssigneeId == assigneeId);
        return new AppliedResolution(filtered);
    }
}