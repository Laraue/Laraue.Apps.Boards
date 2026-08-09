using System.Text.RegularExpressions;
using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Handles "key:BRD-40" — an exact lookup by issue key (space key + issue number), separate
/// from free-text content search. Keeping this as its own token (rather than sniffing free
/// text for a "LETTERS-NUMBER" shape) avoids the ambiguity of someone genuinely searching for
/// that literal text inside issue content.
/// </summary>
public sealed class IssueKeyTokenFilter : IQueryTokenFilter
{
    private static readonly Regex ValueRegex = new(@"^([A-Za-z]+)-(\d+)$", RegexOptions.Compiled);

    public string Key => "key";

    public Task<TokenResolution> ResolveAsync(
        FilterContext context,
        IQueryable<Issue> query,
        string value,
        bool isFinalized,
        CancellationToken ct)
    {
        var match = ValueRegex.Match(value);

        if (match.Success && int.TryParse(match.Groups[2].Value, out var issueNumber))
        {
            var spaceKeyUpper = match.Groups[1].Value.ToUpperInvariant();

            var filtered = query.Where(x =>
                x.Status!.Epic!.Space!.Key == spaceKeyUpper &&
                x.IssueNumber!.Number == issueNumber);

            return Task.FromResult<TokenResolution>(new AppliedResolution(filtered));
        }

        // Anything that doesn't match "LETTERS-NUMBER" is wrong regardless of whether the
        // user is still typing — unlike org:/assignee:, there's no ambiguous "still narrowing
        // down a suggestion" state for a key: it's either shaped right or it isn't, so there's
        // no reason to wait for isFinalized before saying so.
        return Task.FromResult<TokenResolution>(new ErrorResolution(
            "Invalid issue key",
            "Use a format like key:BRD-40."));
    }
}