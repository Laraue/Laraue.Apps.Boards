using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Telegram.Bot.Types.InlineQueryResults;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// The outcome of a single filter resolving a "key:value" token against the current query.
/// Filters construct one of the four derived records directly: <see cref="AppliedResolution"/>,
/// <see cref="SuggestionsResolution"/>, <see cref="PreviewResolution"/>, or
/// <see cref="ErrorResolution"/>.
/// </summary>
public abstract record TokenResolution;

/// <summary>
/// The token was valid and finalized — query now has this filter applied.
/// <paramref name="SelectedOrganizationIds"/> and <paramref name="SelectedSpaceIds"/> are
/// optional: only filters that narrow the search to specific organizations/spaces (org: and
/// space: respectively) populate them, so that later tokens in the same query (e.g.
/// assignee:) can scope their own suggestions/behavior accordingly. Leave both null if the
/// filter doesn't affect that scope.
/// <paramref name="Description"/> is a short, human-readable phrase describing what this
/// filter narrowed to (e.g. "organization starting with \"la\""), used to build a clear
/// "no issues found for X, Y" message if the overall search ends up empty.
/// </summary>
public sealed record AppliedResolution(
    IQueryable<Issue> Query,
    IReadOnlyList<long>? SelectedOrganizationIds = null,
    IReadOnlyList<long>? SelectedSpaceIds = null,
    string? Description = null) : TokenResolution;

/// <summary>
/// A pickable list of candidates — used for the Browse state (empty value, list everything)
/// and the LiveFilter state (partial value, list everything with matches marked) of
/// candidate-list filters (org:, space:, assignee:).
/// </summary>
public sealed record SuggestionsResolution(IReadOnlyList<InlineQueryResult> Results) : TokenResolution;

/// <summary>
/// The value has a complete, valid shape but hasn't been explicitly finalized yet — shown as
/// a single resolved-looking preview (e.g. "Updated after Aug 3, 2026") rather than a picker,
/// since there's nothing to pick from. Also used for the Browse-state format hint on
/// shape-validated filters (key:, upd:) that have no enumerable candidate list at all.
/// </summary>
public sealed record PreviewResolution(string Title, string Message) : TokenResolution;

/// <summary>
/// The value is invalid — either it can never become valid (a genuine typo, shown
/// immediately even mid-typing) or it was finalized without matching anything.
/// </summary>
public sealed record ErrorResolution(string Title, string Message) : TokenResolution;