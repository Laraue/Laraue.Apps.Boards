using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Handles "assignee:me" or "assignee:&lt;display name&gt;". Same contract as
/// org:/space:, with "me" checked first as a permanent reserved exact match. Candidates are
/// scoped to users who can read at least one of the spaces currently in play
/// (<see cref="FilterContext.EffectiveSpaceIds"/>) — not just org membership, since a user
/// might have only a direct per-space grant without org-wide read access.
/// A member can have a different name in each organization, so a candidate is one membership, and
/// a name matches the user's issues only in the organization where they go by that name.
/// </summary>
public sealed class AssigneeTokenFilter(IOptions<AppOptions> options, IAccessService accessService) : IQueryTokenFilter
{
    private readonly record struct UserCandidate(long OrganizationUserId, Guid Id, string DisplayName);

    public string Key => "assignee";

    public async Task<TokenResolution> ResolveAsync(
        FilterContext context,
        IQueryable<Issue> query,
        string value,
        bool isFollowedByAnotherToken,
        CancellationToken ct)
    {
        if (string.Equals(value, "me", StringComparison.OrdinalIgnoreCase))
        {
            var filtered = query.Where(x => x.AssigneeId == context.RequestContext.UserId);
            return new AppliedResolution(filtered, Description: "assignee \"me\"");
        }

        var candidates = await GetCandidateUsersAsync(context, ct);

        if (value.Length == 0)
        {
            return BuildPicker(context, candidates, string.Empty, showWildcardHint: true);
        }

        var isWildcard = value[^1] == TokenSyntax.WildcardSuffix;
        var prefix = isWildcard ? value[..^1] : value;

        if (!isWildcard)
        {
            var exactMatches = candidates
                .Where(u => string.Equals(u.DisplayName, value, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (exactMatches.Length > 0)
            {
                var filtered = FilterByAssignees(context, query, exactMatches);
                return new AppliedResolution(filtered, Description: $"assignee \"{exactMatches[0].DisplayName}\"");
            }
        }

        var prefixMatches = candidates
            .Where(u => u.DisplayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (isWildcard || isFollowedByAnotherToken)
        {
            if (prefixMatches.Length == 0)
            {
                return new ErrorResolution(
                    "User not found",
                    $"No user starting with \"{prefix}\" exists or is accessible to you.");
            }

            var filtered = FilterByAssignees(context, query, prefixMatches);
            return new AppliedResolution(filtered, Description: $"assignee starting with \"{prefix}\"");
        }

        if (prefixMatches.Length == 0)
        {
            return new ErrorResolution(
                "User not found",
                $"No user starting with \"{value}\" exists or is accessible to you.");
        }

        return BuildPicker(context, candidates, value, showWildcardHint: true);
    }

    private TokenResolution BuildPicker(
        FilterContext context,
        IReadOnlyList<UserCandidate> candidates,
        string value,
        bool showWildcardHint)
    {
        var results = new List<InlineQueryResult>();

        var meIsMatch = value.Length > 0 && "me".StartsWith(value, StringComparison.OrdinalIgnoreCase);
        results.Add(new InlineQueryResultArticle(
            "assignee-me",
            meIsMatch ? "✅ me" : "me",
            new InputTextMessageContent(SearchTextFormatter.EscapeMarkdownV2("assignee:me"))
            {
                ParseMode = ParseMode.MarkdownV2
            })
        {
            ThumbnailUrl = options.Value.Icons.User,
            Description = "assignee:me — issues assigned to you"
        });

        // "me" already represents the current user — exclude their own row below, or they'd
        // show up twice.
        // The same user can go by different names in different organizations - one row per name,
        // and a result id has to stay unique within the answer.
        var rowsPerUser = new Dictionary<Guid, int>();
        results.AddRange(candidates
            .Where(u => u.Id != context.RequestContext.UserId)
            .DistinctBy(u => (u.Id, u.DisplayName.ToUpperInvariant()))
            .OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(u =>
            {
                var previousRows = rowsPerUser.GetValueOrDefault(u.Id);
                rowsPerUser[u.Id] = previousRows + 1;
                var resultId = previousRows == 0 ? $"assignee-{u.Id}" : $"assignee-{u.Id}-{previousRows}";

                var isMatch = value.Length > 0 && u.DisplayName.StartsWith(value, StringComparison.OrdinalIgnoreCase);
                var title = isMatch ? $"✅ {u.DisplayName}" : u.DisplayName;

                return (InlineQueryResult)new InlineQueryResultArticle(
                    resultId,
                    title,
                    new InputTextMessageContent(SearchTextFormatter.EscapeMarkdownV2($"assignee:{u.DisplayName}"))
                    {
                        ParseMode = ParseMode.MarkdownV2
                    })
                {
                    ThumbnailUrl = options.Value.Icons.User,
                    Description = $"assignee:{u.DisplayName} — apply this filter"
                };
            }));

        if (showWildcardHint)
        {
            var hintMessage = value.Length == 0
                ? "assignee:"
                : $"assignee:{value}{TokenSyntax.WildcardSuffix} ";

            results.Add(new InlineQueryResultArticle(
                "assignee-hint",
                value.Length == 0 ? "💬 Type to filter" : $"💬 Search all starting with \"{value}\"",
                new InputTextMessageContent(SearchTextFormatter.EscapeMarkdownV2(hintMessage))
                {
                    ParseMode = ParseMode.MarkdownV2
                })
            {
                ThumbnailUrl = options.Value.Icons.Hint,
                Description = value.Length == 0
                    ? "Type a name to filter the list"
                    : $"Add \"{TokenSyntax.WildcardSuffix}\" to search all matches now, or finish typing the exact name"
            });
        }

        return new SuggestionsResolution(results);
    }

    private async Task<IReadOnlyList<UserCandidate>> GetCandidateUsersAsync(
        FilterContext context,
        CancellationToken ct)
    {
        // Scope candidates to the spaces actually in play right now — whatever org:/space:
        // already narrowed earlier in the same query, or every readable space if neither ran
        // yet. This must be space-level, not just org membership: a user (or another org
        // member) might have access to only one space in an org via a direct space
        // permission, without org-wide CanRead — showing every org member here would leak
        // people who can't actually see the space(s) being searched.
        var scopeSpaceIds = context.EffectiveSpaceIds.ToArray();

        var candidates = await accessService.GetVisibleUsers(
            scopeSpaceIds,
            query => query
                .Select(ou => new { ou.Id, ou.UserId, ou.DisplayName })
                .ToListAsyncLinqToDB(ct));

        return candidates
            .Select(u => new UserCandidate(u.Id, u.UserId, u.DisplayName))
            .ToList();
    }

    /// <summary>
    /// Issues assigned to one of <paramref name="matches"/> in the organization of that membership -
    /// the name a user was matched by is theirs only there.
    /// </summary>
    private static IQueryable<Issue> FilterByAssignees(
        FilterContext context,
        IQueryable<Issue> query,
        IReadOnlyCollection<UserCandidate> matches)
    {
        var organizationUserIds = matches.Select(u => u.OrganizationUserId).ToArray();

        return query.Where(x => context.DbContext.OrganizationUsers.Any(ou =>
            organizationUserIds.Contains(ou.Id)
            && ou.UserId == x.AssigneeId
            && ou.OrganizationId == x.Status!.Epic!.Space!.OrganizationId));
    }
}