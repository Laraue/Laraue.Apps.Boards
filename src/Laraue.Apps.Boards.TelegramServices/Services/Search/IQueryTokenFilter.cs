using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Parses and applies one "key:value" query token, e.g. "org:la", "assignee:me", "updated:-7d".
/// Implementations are stateless and safe to reuse across requests — all per-request state
/// lives in <see cref="FilterContext"/> and the token value passed to <see cref="ResolveAsync"/>.
/// </summary>
public interface IQueryTokenFilter
{
    /// <summary>The token key this filter handles, e.g. "org". Matched case-insensitively.</summary>
    string Key { get; }

    /// <summary>
    /// Resolves this filter's token against the current query.
    /// </summary>
    /// <param name="context">Shared per-request data (readable spaces/orgs, db context, etc).</param>
    /// <param name="query">The issue query built up so far by previously-applied filters.</param>
    /// <param name="value">The raw text after "key:", e.g. "la" for "org:la". May be empty.</param>
    /// <param name="isFinalized">
    /// True if the user has moved past this token (typed a trailing space, or it's not the last
    /// token in the query). False means this is the last, still-being-typed token — filters
    /// should offer suggestions here rather than validating strictly.
    /// </param>
    /// <param name="ct"></param>
    Task<TokenResolution> ResolveAsync(
        FilterContext context,
        IQueryable<Issue> query,
        string value,
        bool isFinalized,
        CancellationToken ct);
}