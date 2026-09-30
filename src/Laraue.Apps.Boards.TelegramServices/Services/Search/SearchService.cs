using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Extensions;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Members;
using Laraue.Apps.Boards.TelegramServices.Resources;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.Linq2DB.Extensions;
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

/// <summary>
/// <paramref name="Offset"/> is Telegram's own inline-query pagination cursor - empty on the
/// first page, then whatever this service previously returned as <c>next_offset</c> once the
/// user scrolls the results list further.
/// </summary>
public record SearchRequest(Guid UserId, string InlineQuery, string InlineQueryId, string Offset = "");

public class SearchService(
    DatabaseContext context,
    ILogger<SearchService> logger,
    ITokenFilterRegistry filterRegistry,
    IOptions<AppOptions> options,
    IIssueUrlBuilder issueUrlBuilder,
    IMemberProfileReader memberProfileReader,
    ITelegramBotClient botClient)
    : ISearchService
{
    private const int PageSize = 20;

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
        var readableSpaces = await GetReadableSpacesAsync(readableSpaceIds, ct);
        var filterContext = new FilterContext(
            context, request, readableSpaceIds, readableOrganizations, readableSpaces);

        var (filterTokens, freeTextWords) = QueryTokenParser.Parse(
            inlineQuery,
            filterRegistry.Keys);

        var issuesQuery = context.ActiveIssues()
            .Where(x => readableSpaceIds.Contains(x.Status!.Epic!.SpaceId));

        var isKeyLookup = false;
        var appliedDescriptions = new List<string>();

        foreach (var token in filterTokens)
        {
            // Should always succeed — Parse() only produced this token because the key
            // matched filterRegistry.Keys — but guard defensively rather than assume.
            if (!filterRegistry.TryGet(token.Key, out var filter))
            {
                freeTextWords.Add($"{token.Key}:{token.Value}");
                continue;
            }

            var resolution = await filter.ResolveAsync(filterContext, issuesQuery, token.Value, token.IsFollowedByAnotherToken, ct);

            switch (resolution)
            {
                case AppliedResolution applied:
                    issuesQuery = applied.Query;
                    if (string.Equals(token.Key, "key", StringComparison.OrdinalIgnoreCase))
                        isKeyLookup = true;

                    if (applied.Description is not null)
                    {
                        appliedDescriptions.Add(applied.Description);
                    }

                    if (applied.SelectedOrganizationIds is not null || applied.SelectedSpaceIds is not null)
                    {
                        // A token (org: and/or space:) narrowed organization/space scope —
                        // rebuild the context so later tokens in this same query (e.g.
                        // assignee: after org: or space:) see it. This is what makes tokens
                        // apply sequentially rather than each seeing the same static snapshot.
                        filterContext = filterContext with
                        {
                            SelectedOrganizationIds = applied.SelectedOrganizationIds ?? filterContext.SelectedOrganizationIds,
                            SelectedSpaceIds = applied.SelectedSpaceIds ?? filterContext.SelectedSpaceIds
                        };
                    }

                    break;

                case SuggestionsResolution suggestions:
                    // isPersonal: true — results depend on this user's org access/identity and
                    // must never be served by Telegram's cache to a different user who happens
                    // to type the same query text.
                    await botClient.AnswerInlineQuery(
                        request.InlineQueryId,
                        suggestions.Results,
                        cacheTime: 0,
                        isPersonal: true,
                        cancellationToken: ct);
                    
                    return;

                case PreviewResolution preview:
                    // Complete-but-not-yet-finalized shape (upd:>7d) or a Browse-state format
                    // hint (key:, upd: with nothing typed yet) — shown as a single result the
                    // user isn't meant to tap so much as read, same rendering as a "no
                    // results" placeholder but conceptually distinct (nothing is wrong here).
                    await AnswerNoResults(
                        request.InlineQueryId,
                        $"{token.Key}-preview",
                        preview.Title,
                        preview.Message,
                        ct);

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
            var searchPattern = searchText.AsSearchable();
            issuesQuery = issuesQuery
                .Where(x => x.Title.ILike(searchPattern) || (x.Content != null && x.Content.ILike(searchPattern)));
        }
        else if (!isKeyLookup)
        {
            // Plain equality, not wrapped in a function — stays index-friendly, unlike Trim().
            // Skipped for a key lookup: an exact key match should still show up even if that
            // issue happens to have no content — the key itself is a strong enough signal.
            // A caption-less photo or video has a generated title but no content - still worth listing.
            issuesQuery = issuesQuery.Where(x => x.Title != string.Empty);
        }

        // Telegram's inline query pagination: the client re-sends the same query with `Offset`
        // set to whatever we previously returned as next_offset, once the user scrolls the
        // results list further. `page` stays valid because we only ever hand back page-aligned
        // offsets (below) - a raw, possibly-misaligned offset can't be handed to
        // ShortPaginateLinq2DbAsync directly, since it only understands page numbers.
        var pageOffset = int.TryParse(request.Offset, out var parsedOffset) && parsedOffset >= 0
            ? parsedOffset
            : 0;
        var page = pageOffset / PageSize;

        // ShortPaginateLinq2DbAsync is the same "fetch PageSize + 1 rows to tell whether there's
        // a next page" trick used elsewhere in this codebase's paginated endpoints, instead of a
        // separate COUNT query.
        var paginated = await issuesQuery
            // Most-recently-created first - with no explicit order, Postgres returns rows in
            // whatever order its query plan happens to produce, which isn't guaranteed to be
            // meaningful and can visibly change between otherwise-identical searches (and would
            // make paging incoherent - rows could shuffle between pages between requests).
            .OrderByDescending(x => x.Id)
            .Select(x => new IssueSearchRow
            {
                Key = new IssueKey(x.Status!.Epic!.Space!.Key, x.IssueNumber!.Number),
                Title = x.Title,
                OrganizationName = x.Status.Epic.Space.Organization!.Name,
                OrganizationSlug = x.Status.Epic.Space.Organization!.Slug,
                OrganizationSlugPostfix = x.Status.Epic.Space.Organization!.SlugPostfix,
                ChatTitle = x.TelegramMessage != null ? x.TelegramMessage.LinkedTelegramChat!.Title : null,
                OrganizationId = x.Status.Epic.Space.OrganizationId,
                Sender = x.TelegramMessage != null && x.TelegramMessage.SenderId != null
                    ? new UserDetails { UserId = x.TelegramMessage.SenderId.Value }
                    : null,
                SentAt = x.TelegramMessage != null ? x.TelegramMessage.SentAt : null,
            })
            .ShortPaginateLinq2DbAsync(new PaginationData { Page = page, PerPage = PageSize }, ct);

        var issues = paginated.Data;
        var nextOffset = paginated.HasNextPage ? ((page + 1) * PageSize).ToString() : string.Empty;

        logger.LogInformation(
            "Search text {SearchText:l} (key lookup: {IsKeyLookup}) matched {IssueCount} issue(s) at offset {Offset}",
            searchText, isKeyLookup, issues.Count, pageOffset);

        if (issues.Count == 0)
        {
            var scopeParts = new List<string>(appliedDescriptions);
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                scopeParts.Add($"text \"{searchText}\"");
            }

            var message = scopeParts.Count > 0
                ? $"No issues found for {string.Join(", ", scopeParts)}."
                : "No issues in this scope.";

            await AnswerNoResults(request.InlineQueryId, "no-issues", "No issues found", message, ct);
            return;
        }

        // A sender is shown by their name in the issue's organization.
        await memberProfileReader.EnrichUsers(
            issues,
            x => x.OrganizationId,
            x => x.Sender,
            ct);

        var result = new List<InlineQueryResult>();
        foreach (var issue in issues)
        {
            // A result shows the issue's title only, never its content - the search still matches
            // the content too, so an issue can be found by a word that only its body has.
            var title = SearchTextFormatter.NormalizeWhitespace(issue.Title);

            if (string.IsNullOrWhiteSpace(title))
            {
                if (!isKeyLookup)
                {
                    // Don't send Telegram a message with no text - it can't render that and shows
                    // a broken "open bot privately" placeholder instead - so skip this result.
                    logger.LogWarning("Issue {IssueKey}: title is empty, skipping", issue.Key);
                    continue;
                }

                // Found by exact key - still show it, with a placeholder instead of a blank preview.
                title = "(no description)";
            }

            var issueUrl = issueUrlBuilder.Build(issue.OrganizationSlug, issue.OrganizationSlugPostfix, issue.Key);
            var footer = IssuePreviewFormatter.BuildSourceFooter(
                issue.ChatTitle,
                issue.Sender?.DisplayName,
                issue.SentAt);

            // The link lives on a button, not in the text - buttons render reliably regardless of
            // MarkdownV2 escaping, whereas an in-text [text](url) link depends on every character
            // around it being escaped exactly right or Telegram shows the raw syntax.
            var messageText =
                IssuePreviewFormatter.BuildHeader(issue.Key, issue.OrganizationName) + "\n" +
                SearchTextFormatter.EscapeMarkdownV2(title) +
                (footer is not null ? "\n" + footer : string.Empty);

            result.Add(
                new InlineQueryResultArticle(
                    issue.Key.ToString(),
                    $"{issue.Key} · {issue.OrganizationName}",
                    new InputTextMessageContent(messageText)
                    {
                        ParseMode = ParseMode.MarkdownV2
                    })
                {
                    Description = title,
                    // Without this, Telegram falls back to a grey placeholder tile with just the
                    // first letter of the title - setting a real icon here is what makes the
                    // mobile results list show an actual image instead.
                    ThumbnailUrl = options.Value.Icons.Issue,
                    ReplyMarkup = new InlineKeyboardMarkup(
                        InlineKeyboardButton.WithUrl(Phrases.OpenIssueButton, issueUrl))
                });
        }

        // isPersonal: true — see note above; the same reasoning applies to every branch
        // that answers an inline query, not just the suggestions one. nextOffset left empty
        // means "no more pages" to Telegram - it only requests another page when we hand back
        // a non-empty cursor here.
        await botClient.AnswerInlineQuery(
            request.InlineQueryId,
            result,
            cacheTime: 0,
            isPersonal: true,
            nextOffset: nextOffset,
            cancellationToken: ct);
    }

    private async Task<long[]> GetReadableSpaceIdsAsync(
        SearchRequest requestContext,
        CancellationToken ct)
    {
        var organizationsData = context.ActiveOrganizationUsers()
            .Where(x => x.UserId == requestContext.UserId)
            .Select(x => new { x.CanRead, x.OrganizationId });

        return await context.ActiveSpaces()
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

        return await context.ActiveOrganizations()
            .Where(s => organizationIds.Contains(s.Id))
            .Select(s => new OrganizationInfo(
                s.Id,
                s.Name,
                s.Slug)) // adjust to your actual "key" field if different
            .Distinct()
            .ToListAsyncLinqToDB(ct);
    }

    private async Task<IReadOnlyList<SpaceInfo>> GetReadableSpacesAsync(
        long[] readableSpaceIds,
        CancellationToken ct)
    {
        return await context.Spaces
            .Where(s => readableSpaceIds.Contains(s.Id))
            .Select(s => new SpaceInfo(
                s.Id,
                s.Key,
                s.Name, // adjust if Space doesn't have a Name property distinct from Key
                s.OrganizationId))
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

        // isPersonal: true — see note above.
        await botClient.AnswerInlineQuery(
            inlineQueryId,
            [placeholder],
            cacheTime: 0,
            isPersonal: true,
            cancellationToken: ct);
    }
}

/// <summary>One issue found by an inline search, as the query projects it.</summary>
internal sealed class IssueSearchRow
{
    public required IssueKey Key { get; init; }
    public required string Title { get; init; }
    public required string OrganizationName { get; init; }
    public required string OrganizationSlug { get; init; }
    public required string OrganizationSlugPostfix { get; init; }
    public required string? ChatTitle { get; init; }
    public required long OrganizationId { get; init; }
    public required UserDetails? Sender { get; init; }
    public required DateTime? SentAt { get; init; }
}
