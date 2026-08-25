using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Handles "assignee:me" or "assignee:&lt;telegram username&gt;". Same contract as
/// org:/space:, with "me" checked first as a permanent reserved exact match. Candidates are
/// scoped to users who can read at least one of the spaces currently in play
/// (<see cref="FilterContext.EffectiveSpaceIds"/>) — not just org membership, since a user
/// might have only a direct per-space grant without org-wide read access.
/// </summary>
public sealed class AssigneeTokenFilter(IOptions<AppOptions> options, IAccessService accessService) : IQueryTokenFilter
{
    private readonly record struct UserCandidate(Guid Id, string Username);

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
            var exactMatch = candidates
                .FirstOrDefault(u => string.Equals(u.Username, value, StringComparison.OrdinalIgnoreCase));

            if (exactMatch.Username is not null)
            {
                var filtered = query.Where(x => x.AssigneeId == exactMatch.Id);
                return new AppliedResolution(filtered, Description: $"assignee \"{exactMatch.Username}\"");
            }
        }

        var prefixMatchIds = candidates
            .Where(u => u.Username.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(u => (Guid?)u.Id)
            .ToArray();

        if (isWildcard || isFollowedByAnotherToken)
        {
            if (prefixMatchIds.Length == 0)
            {
                return new ErrorResolution(
                    "User not found",
                    $"No user starting with \"{prefix}\" exists or is accessible to you.");
            }

            var filtered = query.Where(x => prefixMatchIds.Contains(x.AssigneeId));
            return new AppliedResolution(filtered, Description: $"assignee starting with \"{prefix}\"");
        }

        if (prefixMatchIds.Length == 0)
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
        results.AddRange(candidates
            .Where(u => u.Id != context.RequestContext.UserId)
            .OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(u =>
            {
                var isMatch = value.Length > 0 && u.Username.StartsWith(value, StringComparison.OrdinalIgnoreCase);
                var title = isMatch ? $"✅ {u.Username}" : u.Username;

                return (InlineQueryResult)new InlineQueryResultArticle(
                    $"assignee-{u.Id}",
                    title,
                    new InputTextMessageContent(SearchTextFormatter.EscapeMarkdownV2($"assignee:{u.Username}"))
                    {
                        ParseMode = ParseMode.MarkdownV2
                    })
                {
                    ThumbnailUrl = options.Value.Icons.User,
                    Description = $"assignee:{u.Username} — apply this filter"
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
                    ? "Type a username to filter the list"
                    : $"Add \"{TokenSyntax.WildcardSuffix}\" to search all matches now, or finish typing the exact username"
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
                .Where(ou => ou.User!.TelegramUserName != null)
                .Select(ou => new { ou.UserId, ou.User!.TelegramUserName })
                .Distinct()
                .ToListAsyncLinqToDB(ct));

        return candidates
            .Select(u => new UserCandidate(u.UserId, u.TelegramUserName!))
            .ToList();
    }
}