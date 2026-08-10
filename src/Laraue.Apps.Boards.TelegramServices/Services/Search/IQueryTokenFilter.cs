using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Parses and applies one "key:value" query token, e.g. "org:la", "assignee:me", "upd:>7d".
///
/// Common shape every filter follows (details vary by whether the filter has an enumerable
/// candidate list — org:/space:/assignee: — or is purely shape-validated — key:/upd:):
///
///   - empty value                              -> browse everything / show a format hint
///   - exact match (or, for key:/upd:, a
///     complete valid shape)                    -> apply immediately, unambiguous
///   - explicit trailing TokenSyntax.WildcardSuffix
///     (candidate-list filters only)            -> apply as a prefix search, even to zero results
///   - followed by another token                -> apply as a best-effort prefix search
///     (the user has moved on; not exact, but nothing more will be typed into this token)
///   - otherwise (still the last token, no exact
///     match, no wildcard)                      -> show a picker / live validity preview
///
/// Implementations are stateless and safe to reuse across requests — all per-request state
/// lives in <see cref="FilterContext"/> and the values passed in.
/// </summary>
public interface IQueryTokenFilter
{
    /// <summary>The token key this filter handles, e.g. "org". Matched case-insensitively.</summary>
    string Key { get; }

    /// <param name="context">Shared per-request data.</param>
    /// <param name="query">The issue query built up so far by previously-applied filters.</param>
    /// <param name="value">The raw text after "key:". May be empty, and may end with a wildcard marker.</param>
    /// <param name="isFollowedByAnotherToken">
    /// True if another word/token follows this one in the raw query — the one reliable
    /// "user has moved on" signal available, since trailing whitespace isn't preserved.
    /// </param>
    Task<TokenResolution> ResolveAsync(
        FilterContext context,
        IQueryable<Issue> query,
        string value,
        bool isFollowedByAnotherToken,
        CancellationToken ct);
}