using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Extensions;
using Laraue.Apps.Boards.Services;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

public interface ISearchService
{
    Task HandleInlineSearchQuery(SearchRequest request, CancellationToken ct);
}

public record SearchRequest(Guid UserId, string InlineQuery, string InlineQueryId);

public class SearchService(
    DatabaseContext context,
    ILogger<SearchService> logger,
    ITokenFilterRegistry filterRegistry,
    IOptions<AppOptions> options,
    ITelegramBotClient botClient)
    : ISearchService
{
    private const int FragmentContextChars = 70;

    public async Task HandleInlineSearchQuery(SearchRequest request, CancellationToken ct)
    {
        var readableSpaceIds = await GetReadableSpaceIdsAsync(request, ct);
        var inlineQuery = request.InlineQuery;

        logger.LogInformation(
            "Inline search by user {UserId}: raw query {RawQuery:l}",
            request.UserId,
            inlineQuery);

        if (readableSpaceIds.Length == 0)
        {
            await AnswerNoResults(
                request.InlineQueryId,
                "no-spaces",
                "No accessible spaces",
                "You don't have read access to any spaces yet.",
                ct);
            
            return;
        }

        var readableOrganizations = await GetReadableOrganizationsAsync(readableSpaceIds, ct);
        var filterContext = new FilterContext(context, request, readableSpaceIds, readableOrganizations);

        var (filterTokens, freeTextWords) = QueryTokenParser.Parse(
            inlineQuery,
            filterRegistry.Keys);

        var issuesQuery = context.Issues
            .Where(x => readableSpaceIds.Contains(x.Status!.Epic!.SpaceId));

        var isKeyLookup = false;

        foreach (var token in filterTokens)
        {
            // Should always succeed — Parse() only produced this token because the key
            // matched filterRegistry.Keys — but guard defensively rather than assume.
            if (!filterRegistry.TryGet(token.Key, out var filter))
            {
                freeTextWords.Add($"{token.Key}:{token.Value}");
                continue;
            }

            var resolution = await filter.ResolveAsync(filterContext, issuesQuery, token.Value, token.IsFinalized, ct);

            switch (resolution)
            {
                case AppliedResolution applied:
                    issuesQuery = applied.Query;
                    if (string.Equals(token.Key, "key", StringComparison.OrdinalIgnoreCase))
                        isKeyLookup = true;
                    
                    break;

                case SuggestionsResolution { Results.Count: 0 }:
                    // Filter had nothing to suggest for this partial token (e.g. a bare numeric
                    // filter) — fall back to treating it as free text instead of showing nothing.
                    freeTextWords.Add($"{token.Key}:{token.Value}");
                    break;

                case SuggestionsResolution suggestions:
                    await botClient.AnswerInlineQuery(
                        request.InlineQueryId,
                        suggestions.Results,
                        cacheTime: 0,
                        cancellationToken: ct);
                    
                    return;

                case ErrorResolution error:
                    await AnswerNoResults(
                        request.InlineQueryId,
                        $"{token.Key}-error",
                        error.Title,
                        error.Message, ct);
                    
                    return;
            }
        }

        var searchText = string.Join(' ', freeTextWords).Trim();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            // No extra empty-content guard needed here: ILIKE against a non-empty pattern
            // can never match empty content, so this branch already excludes it for free.
            issuesQuery = issuesQuery
                .Where(x => x.Content != null)
                .Where(x => x.Content!.ILike(searchText.AsSearchable()));
        }
        else if (!isKeyLookup)
        {
            // Plain equality, not wrapped in a function — stays index-friendly, unlike Trim().
            // Skipped for a key lookup: an exact key match should still show up even if that
            // issue happens to have no content — the key itself is a strong enough signal.
            issuesQuery = issuesQuery.Where(x => x.Content != null && x.Content != string.Empty);
        }

        var issues = await issuesQuery
            .Select(x => new
            {
                Key = new IssueKey(x.Status!.Epic!.Space!.Key, x.IssueNumber!.Number),
                x.Content, // nullable — a key-matched issue may have no content
                OrganizationName = x.Status.Epic.Space.Organization!.Name,
                OrganizationSlug = x.Status.Epic.Space.Organization!.Slug,
                OrganizationSlugPostfix = x.Status.Epic.Space.Organization!.SlugPostfix,
            })
            .Take(5)
            .ToListAsyncLinqToDB(ct);

        logger.LogInformation(
            "Search text {SearchText:l} (key lookup: {IsKeyLookup}) matched {IssueCount} issue(s)",
            searchText, isKeyLookup, issues.Count);

        if (issues.Count == 0)
        {
            await AnswerNoResults(
                request.InlineQueryId,
                "no-issues",
                "No issues found",
                string.IsNullOrWhiteSpace(searchText)
                    ? "No issues in this scope."
                    : $"Nothing matched \"{searchText}\".",
                ct);
            return;
        }

        var result = new List<InlineQueryResult>();
        foreach (var issue in issues)
        {
            var normalizedContent = SearchTextFormatter.CleanForPreview(issue.Content ?? string.Empty);

            if (normalizedContent.Length == 0)
            {
                if (isKeyLookup)
                {
                    // Found by exact key even though it has no content — still show it,
                    // just with a placeholder instead of a blank preview.
                    normalizedContent = "(no description)";
                }
                else
                {
                    // Whitespace-only content (passes the DB's plain != "" check but normalizes to
                    // nothing) or the empty-content edge case in general. Don't send Telegram a
                    // message with no text — it can't render that and shows a broken
                    // "open bot privately" placeholder instead — so just skip this result.
                    logger.LogWarning("Issue {IssueKey}: content is empty after normalization, skipping", issue.Key);
                    continue;
                }
            }

            var fragment = ContentFragment.Extract(normalizedContent, searchText, FragmentContextChars);

            if (!string.IsNullOrWhiteSpace(searchText) && fragment.Match.Length == 0)
            {
                // The DB matched this issue via ILIKE, but our own IndexOf couldn't find the
                // term in the same (normalized) content — this is the mismatch we're chasing.
                // Log enough to diagnose (length + a short snippet) without dumping full,
                // potentially large or sensitive issue content into logs.
                var snippetLength = Math.Min(200, normalizedContent.Length);
                logger.LogWarning(
                    "Issue {IssueKey}: ILIKE matched {SearchText:l} but IndexOf did not. " +
                    "ContentLength={ContentLength}, Snippet={Snippet:l}",
                    issue.Key, searchText, normalizedContent.Length, normalizedContent[..snippetLength]);
            }

            var orgKey = $"{issue.OrganizationSlug}-{issue.OrganizationSlugPostfix}";
            var issueUrl = $"{options.Value.Url}/organizations/{orgKey}/issues/{issue.Key}";

            // The link lives on a button, not in the text — buttons render reliably regardless
            // of MarkdownV2 escaping, whereas an in-text [text](url) link depends on every
            // character around it being escaped exactly right or Telegram shows the raw syntax.
            var messageText =
                $"📋 *{SearchTextFormatter.EscapeMarkdownV2(issue.Key.ToString())}* · {SearchTextFormatter.EscapeMarkdownV2(issue.OrganizationName)}\n" +
                fragment.ToMarkdownV2();

            result.Add(
                new InlineQueryResultArticle(
                    issue.Key.ToString(),
                    $"{issue.Key} · {issue.OrganizationName}",
                    new InputTextMessageContent(messageText)
                    {
                        ParseMode = ParseMode.MarkdownV2
                    })
                {
                    Description = fragment.ToPlainText(),
                    ReplyMarkup = new InlineKeyboardMarkup(
                        InlineKeyboardButton.WithUrl("🔗 Open issue", issueUrl))
                });
        }

        await botClient.AnswerInlineQuery(
            request.InlineQueryId,
            result,
            cacheTime: 0,
            cancellationToken: ct);
    }

    private async Task<long[]> GetReadableSpaceIdsAsync(
        SearchRequest requestContext,
        CancellationToken ct)
    {
        var organizationsData = context.OrganizationUsers
            .Where(x => x.UserId == requestContext.UserId)
            .Select(x => new { x.CanRead, x.OrganizationId });

        return await context.Spaces
            .InnerJoin(
                organizationsData,
                (space, organizationData) => space.OrganizationId == organizationData.OrganizationId,
                (space, organizationData) => new { space, organizationData })
            .LeftJoin(
                context.DirectSpacePermissions,
                (space, directSpacePermission) => space.space.Id == directSpacePermission.SpaceId,
                (space, directSpacePermission) => new { space, directSpacePermission })
            .Where(x => x.directSpacePermission.CanRead || x.space.organizationData.CanRead)
            .Select(x => x.space.space.Id)
            .ToArrayAsyncEF(ct);
    }

    private async Task<IReadOnlyList<OrganizationInfo>> GetReadableOrganizationsAsync(
        long[] readableSpaceIds,
        CancellationToken ct)
    {
        var organizationIds = context.Spaces
            .Where(s => readableSpaceIds.Contains(s.Id))
            .Select(x => x.OrganizationId)
            .Distinct();

        return await context.Organizations
            .Where(s => organizationIds.Contains(s.Id))
            .Select(s => new OrganizationInfo(
                s.Id,
                s.Name,
                s.Slug)) // adjust to your actual "key" field if different
            .Distinct()
            .ToListAsyncLinqToDB(ct);
    }

    private async Task AnswerNoResults(
        string inlineQueryId,
        string resultId,
        string title,
        string message,
        CancellationToken ct)
    {
        var placeholder = new InlineQueryResultArticle(
            resultId,
            title,
            new InputTextMessageContent(SearchTextFormatter.EscapeMarkdownV2(message))
            {
                ParseMode = ParseMode.MarkdownV2
            })
        {
            Description = message
        };

        await botClient.AnswerInlineQuery(
            inlineQueryId,
            [placeholder],
            cacheTime: 0,
            cancellationToken: ct);
    }
}