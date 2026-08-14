using Laraue.Apps.Boards.DataAccess.Models;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Handles "space:&lt;value&gt;". Same contract as <see cref="OrganizationTokenFilter"/>. If an org:
/// token already narrowed the search earlier in the same query, matching/browsing are scoped
/// to spaces within that organization only.
/// </summary>
public sealed class SpaceTokenFilter(IOptions<AppOptions> options) : IQueryTokenFilter
{
    public string Key => "space";

    public Task<TokenResolution> ResolveAsync(
        FilterContext context,
        IQueryable<Issue> query,
        string value,
        bool isFollowedByAnotherToken,
        CancellationToken ct)
    {
        var candidates = CandidateSpaces(context);

        if (value.Length == 0)
        {
            return Task.FromResult(BuildPicker(candidates, string.Empty, showWildcardHint: true));
        }

        var isWildcard = value[^1] == TokenSyntax.WildcardSuffix;
        var prefix = isWildcard ? value[..^1] : value;

        if (!isWildcard)
        {
            var exactMatch = candidates
                .FirstOrDefault(s => string.Equals(s.Key, value, StringComparison.OrdinalIgnoreCase));

            if (exactMatch is not null)
            {
                var filtered = query.Where(x => x.Status!.Epic!.SpaceId == exactMatch.Id);
                return Task.FromResult<TokenResolution>(new AppliedResolution(
                    filtered, SelectedSpaceIds: [exactMatch.Id], Description: $"space \"{exactMatch.Key}\""));
            }
        }

        var prefixMatchIds = candidates
            .Where(s => s.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Id)
            .ToArray();

        if (isWildcard || isFollowedByAnotherToken)
        {
            if (prefixMatchIds.Length == 0)
            {
                return Task.FromResult<TokenResolution>(new ErrorResolution(
                    "Space not found",
                    $"No space key starting with \"{prefix}\" exists or is accessible to you."));
            }

            var filtered = query.Where(x => prefixMatchIds.Contains(x.Status!.Epic!.SpaceId));
            return Task.FromResult<TokenResolution>(new AppliedResolution(
                filtered, SelectedSpaceIds: prefixMatchIds, Description: $"space starting with \"{prefix}\""));
        }

        if (prefixMatchIds.Length == 0)
        {
            return Task.FromResult<TokenResolution>(new ErrorResolution(
                "Space not found",
                $"No space key starting with \"{value}\" exists or is accessible to you."));
        }

        return Task.FromResult(BuildPicker(candidates, value, showWildcardHint: true));
    }

    private static IReadOnlyList<SpaceInfo> CandidateSpaces(FilterContext context) =>
        (context.SelectedOrganizationIds is { } orgIds
            ? context.ReadableSpaces.Where(s => orgIds.Contains(s.OrganizationId))
            : context.ReadableSpaces)
        .ToList();

    private TokenResolution BuildPicker(IReadOnlyList<SpaceInfo> candidates, string value, bool showWildcardHint)
    {
        var results = candidates
            .Select(s =>
            {
                var isMatch = value.Length > 0 && s.Key.StartsWith(value, StringComparison.OrdinalIgnoreCase);
                var title = isMatch ? $"✅ {s.Name}" : s.Name;

                return (InlineQueryResult)new InlineQueryResultArticle(
                    $"space-{s.Id}",
                    title,
                    new InputTextMessageContent(SearchTextFormatter.EscapeMarkdownV2($"space:{s.Key}"))
                    {
                        ParseMode = ParseMode.MarkdownV2
                    })
                {
                    ThumbnailUrl = options.Value.Icons.Space,
                    Description = $"space:{s.Key} — apply this filter"
                };
            })
            .Take(9)
            .ToList();

        if (showWildcardHint)
        {
            var hintMessage = value.Length == 0
                ? "space:"
                : $"space:{value}{TokenSyntax.WildcardSuffix} ";

            results.Add(new InlineQueryResultArticle(
                "space-hint",
                value.Length == 0 ? "💬 Type to filter" : $"💬 Search all starting with \"{value}\"",
                new InputTextMessageContent(SearchTextFormatter.EscapeMarkdownV2(hintMessage))
                {
                    ParseMode = ParseMode.MarkdownV2
                })
            {
                ThumbnailUrl = options.Value.Icons.Hint,
                Description = value.Length == 0
                    ? "Type a space's key to filter the list"
                    : $"Add \"{TokenSyntax.WildcardSuffix}\" to search all matches now, or finish typing the exact key"
            });
        }

        return new SuggestionsResolution(results);
    }
}