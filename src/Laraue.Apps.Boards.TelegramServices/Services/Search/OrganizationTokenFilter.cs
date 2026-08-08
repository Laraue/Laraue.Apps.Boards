using Laraue.Apps.Boards.DataAccess.Models;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Handles "org:&lt;prefix&gt;". Finalized tokens match any readable organization whose key
/// starts with the given prefix (case-insensitive); an unfinalized token offers matching
/// organizations by name or key as suggestions.
/// </summary>
public sealed class OrganizationTokenFilter : IQueryTokenFilter
{
    public string Key => "org";

    public Task<TokenResolution> ResolveAsync(
        FilterContext context,
        IQueryable<Issue> query,
        string value,
        bool isFinalized,
        CancellationToken ct)
    {
        if (!isFinalized)
        {
            return Task.FromResult(BuildSuggestions(context, value));
        }

        var matchedOrgIds = context.ReadableOrganizations
            .Where(o => o.Slug.StartsWith(value, StringComparison.OrdinalIgnoreCase))
            .Select(o => o.Id)
            .ToArray();

        if (matchedOrgIds.Length == 0)
        {
            return Task.FromResult<TokenResolution>(new ErrorResolution(
                "Organization not found",
                $"No organization key starting with \"{value}\" exists or is accessible to you."));
        }

        var filtered = query.Where(x => matchedOrgIds.Contains(x.Status!.Epic!.Space!.OrganizationId));
        return Task.FromResult<TokenResolution>(new AppliedResolution(filtered));
    }

    private static TokenResolution BuildSuggestions(FilterContext context, string value)
    {
        var suggestions = context.ReadableOrganizations
            .Where(o => value.Length == 0
                || o.Name.Contains(value, StringComparison.OrdinalIgnoreCase)
                || o.Slug.Contains(value, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();

        if (suggestions.Count == 0)
        {
            return new ErrorResolution(
                "No matching organization",
                $"No organization key or name matches \"{value}\".");
        }

        var results = suggestions.Select(o => (InlineQueryResult)new InlineQueryResultArticle(
            $"org-{o.Id}",
            o.Name,
            new InputTextMessageContent(SearchTextFormatter.EscapeMarkdownV2($"org:{o.Slug} "))
            {
                ParseMode = ParseMode.MarkdownV2
            })
        {
            Description = $"org:{o.Slug} — type this to filter"
        }).ToList();

        return new SuggestionsResolution(results);
    }
}