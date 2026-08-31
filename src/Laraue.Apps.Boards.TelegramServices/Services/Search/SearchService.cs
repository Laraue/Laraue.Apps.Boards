using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Extensions;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices.Resources;
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

        var issuesQuery = context.Issues
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

        // Telegram's inline query pagination: the client re-sends the same query with `Offset`
        // set to whatever we previously returned as next_offset, once the user scrolls the
        // results list further. Fetching one row past the page tells us whether there's a next
        // page to advertise, without a separate COUNT query.
        var pageOffset = int.TryParse(request.Offset, out var parsedOffset) && parsedOffset >= 0
            ? parsedOffset
            : 0;

        var issues = await issuesQuery
            // Most-recently-created first - with no explicit order, Postgres returns rows in
            // whatever order its query plan happens to produce, which isn't guaranteed to be
            // meaningful and can visibly change between otherwise-identical searches (and would
            // make paging incoherent - rows could shuffle between pages between requests).
            .OrderByDescending(x => x.Id)
            .Select(x => new
            {
                Key = new IssueKey(x.Status!.Epic!.Space!.Key, x.IssueNumber!.Number),
                x.Content, // nullable — a key-matched issue may have no content
                OrganizationName = x.Status.Epic.Space.Organization!.Name,
                OrganizationSlug = x.Status.Epic.Space.Organization!.Slug,
                OrganizationSlugPostfix = x.Status.Epic.Space.Organization!.SlugPostfix,
                ChatTitle = x.TelegramMessage != null ? x.TelegramMessage.LinkedTelegramChat!.Title : null,
                SenderName = x.TelegramMessage != null && x.TelegramMessage.Sender != null
                    ? (x.TelegramMessage.Sender.TelegramUserName ?? x.TelegramMessage.Sender.TelegramFirstName)
                    : null,
                SentAt = x.TelegramMessage != null ? x.TelegramMessage.SentAt : null,
            })
            .Skip(pageOffset)
            .Take(PageSize + 1)
            .ToListAsyncLinqToDB(ct);

        var hasNextPage = issues.Count > PageSize;
        if (hasNextPage)
        {
            issues.RemoveAt(issues.Count - 1);
        }

        var nextOffset = hasNextPage ? (pageOffset + PageSize).ToString() : string.Empty;

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

        var result = new List<InlineQueryResult>();
        foreach (var issue in issues)
        {
            // Only strip decorative runs here, not the full CleanForPreview whitespace collapse -
            // collapsing newlines would merge every paragraph into one line before it reaches
            // TelegramMarkdownFormatter, which relies on line boundaries to keep an inline
            // span's opening/closing marker from pairing up with an unrelated stray marker in a
            // totally different paragraph (turning most of the message bold by accident). The
            // structure needs to survive all the way to ToMarkdownV2() - see GetIssuePreview in
            // TelegramSaveMessageService, which never flattens for the same reason.
            var rawContent = SearchTextFormatter.RemoveDecorativeRuns(issue.Content ?? string.Empty);

            if (string.IsNullOrWhiteSpace(rawContent))
            {
                if (isKeyLookup)
                {
                    // Found by exact key even though it has no content — still show it,
                    // just with a placeholder instead of a blank preview.
                    rawContent = "(no description)";
                }
                else
                {
                    // Whitespace-only content (passes the DB's plain != "" check) or the
                    // empty-content edge case in general. Don't send Telegram a message with no
                    // text — it can't render that and shows a broken "open bot privately"
                    // placeholder instead — so just skip this result.
                    logger.LogWarning("Issue {IssueKey}: content is empty, skipping", issue.Key);
                    continue;
                }
            }

            var fragment = ContentFragment.Extract(rawContent, searchText, IssuePreviewFormatter.FragmentContextChars);

            if (!string.IsNullOrWhiteSpace(searchText) && fragment.Match.Length == 0)
            {
                // The DB matched this issue via ILIKE, but our own IndexOf couldn't find the
                // term in the same content — this is the mismatch we're chasing. Log enough to
                // diagnose (length + a short snippet) without dumping full, potentially large or
                // sensitive issue content into logs.
                var snippetLength = Math.Min(200, rawContent.Length);
                logger.LogWarning(
                    "Issue {IssueKey}: ILIKE matched {SearchText:l} but IndexOf did not. " +
                    "ContentLength={ContentLength}, Snippet={Snippet:l}",
                    issue.Key, searchText, rawContent.Length, rawContent[..snippetLength]);
            }

            var issueUrl = issueUrlBuilder.Build(issue.OrganizationSlug, issue.OrganizationSlugPostfix, issue.Key);
            var footer = IssuePreviewFormatter.BuildSourceFooter(issue.ChatTitle, issue.SenderName, issue.SentAt);

            // The text actually posted to the chat once the user taps this result
            // (InputTextMessageContent) always starts from the beginning of the issue, like
            // /save and /info do - centering on the match (via `fragment`) only makes sense
            // while still choosing between results in the dropdown. Once posted, there's no
            // reader left for whom "here's where your search term was" means anything, and
            // starting mid-issue is just confusing on its own.
            var messagePreview = ContentFragment.Extract(rawContent, searchText: string.Empty, IssuePreviewFormatter.FragmentContextChars);

            // The link lives on a button, not in the text — buttons render reliably regardless
            // of MarkdownV2 escaping, whereas an in-text [text](url) link depends on every
            // character around it being escaped exactly right or Telegram shows the raw syntax.
            var messageText =
                IssuePreviewFormatter.BuildHeader(issue.Key, issue.OrganizationName) + "\n" +
                messagePreview.ToMarkdownV2() +
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
                    // Normalized only here, after extraction/truncation - the dropdown entry is
                    // a single-line UI element regardless, so collapsing any leftover newlines
                    // in this already-bounded snippet is safe (unlike normalizing the whole
                    // content before ToMarkdownV2() above).
                    Description = SearchTextFormatter.NormalizeWhitespace(fragment.ToPlainText()),
                    // Without this, Telegram falls back to a grey placeholder tile with just
                    // the first letter of the title — setting a real icon here is what makes
                    // the mobile results list show an actual image instead.
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