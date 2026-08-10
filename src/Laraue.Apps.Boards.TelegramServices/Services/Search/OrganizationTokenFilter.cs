using Laraue.Apps.Boards.DataAccess.Models;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Handles "org:&lt;value&gt;". An exact (case-insensitive) key match applies immediately —
/// unambiguous, nothing more to wait for. A trailing "*" (org:la*) explicitly requests a
/// prefix search and applies immediately regardless of match count, even zero. Otherwise, if
/// another token follows, applies as a best-effort prefix search (the user has moved on).
/// Failing all of that, shows a picker of matching candidates with a hint teaching the "*"
/// shortcut — or errors immediately if literally nothing matches even by prefix, since a
/// zero-match prefix can never become non-zero by typing more of the same prefix.
/// </summary>
public sealed class OrganizationTokenFilter : IQueryTokenFilter
{
    public string Key => "org";

    public Task<TokenResolution> ResolveAsync(
        FilterContext context,
        IQueryable<Issue> query,
        string value,
        bool isFollowedByAnotherToken,
        CancellationToken ct)
    {
        if (value.Length == 0)
        {
            return Task.FromResult(BuildPicker(context, string.Empty, showWildcardHint: true));
        }

        var isWildcard = value[^1] == TokenSyntax.WildcardSuffix;
        var prefix = isWildcard ? value[..^1] : value;

        if (!isWildcard)
        {
            var exactMatch = context.ReadableOrganizations
                .FirstOrDefault(o => string.Equals(o.Slug, value, StringComparison.OrdinalIgnoreCase));

            if (exactMatch is not null)
            {
                var filtered = query.Where(x => x.Status!.Epic!.Space!.OrganizationId == exactMatch.Id);
                return Task.FromResult<TokenResolution>(new AppliedResolution(
                    filtered, [exactMatch.Id], Description: $"organization \"{exactMatch.Slug}\""));
            }
        }

        var prefixMatchIds = context.ReadableOrganizations
            .Where(o => o.Slug.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(o => o.Id)
            .ToArray();

        if (isWildcard || isFollowedByAnotherToken)
        {
            if (prefixMatchIds.Length == 0)
            {
                return Task.FromResult<TokenResolution>(new ErrorResolution(
                    "Organization not found",
                    $"No organization key starting with \"{prefix}\" exists or is accessible to you."));
            }

            var filtered = query.Where(x => prefixMatchIds.Contains(x.Status!.Epic!.Space!.OrganizationId));
            return Task.FromResult<TokenResolution>(new AppliedResolution(
                filtered, prefixMatchIds, Description: $"organization starting with \"{prefix}\""));
        }

        if (prefixMatchIds.Length == 0)
        {
            return Task.FromResult<TokenResolution>(new ErrorResolution(
                "Organization not found",
                $"No organization key starting with \"{value}\" exists or is accessible to you."));
        }

        return Task.FromResult(BuildPicker(context, value, showWildcardHint: true));
    }

    private static TokenResolution BuildPicker(FilterContext context, string value, bool showWildcardHint)
    {
        var results = context.ReadableOrganizations
            .Select(o =>
            {
                var isMatch = value.Length > 0 && o.Slug.StartsWith(value, StringComparison.OrdinalIgnoreCase);
                var title = isMatch ? $"✅ {o.Name}" : o.Name;

                return (InlineQueryResult)new InlineQueryResultArticle(
                    $"org-{o.Id}",
                    title,
                    new InputTextMessageContent(SearchTextFormatter.EscapeMarkdownV2($"org:{o.Slug}"))
                    {
                        ParseMode = ParseMode.MarkdownV2
                    })
                {
                    Description = $"org:{o.Slug} — apply this filter"
                };
            })
            .Take(9)
            .ToList();

        if (showWildcardHint)
        {
            var hintMessage = value.Length == 0
                ? "org:"
                : $"org:{value}{TokenSyntax.WildcardSuffix} ";

            results.Add(new InlineQueryResultArticle(
                "org-hint",
                value.Length == 0 ? "💬 Type to filter" : $"💬 Search all starting with \"{value}\"",
                new InputTextMessageContent(SearchTextFormatter.EscapeMarkdownV2(hintMessage))
                {
                    ParseMode = ParseMode.MarkdownV2
                })
            {
                Description = value.Length == 0
                    ? "Type an organization's key to filter the list"
                    : $"Add \"{TokenSyntax.WildcardSuffix}\" to search all matches now, or finish typing the exact key"
            });
        }

        return new SuggestionsResolution(results);
    }
}