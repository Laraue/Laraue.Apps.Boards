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
            // The value already has the complete "LETTERS-NUMBER" shape — resolve immediately,
            // even without a trailing space. Unlike org:/assignee:, there's no useful "browse
            // matching keys" suggestion list here, so waiting for isFinalized would just make
            // an already-complete key search silently do nothing until the user adds a space
            // or another word (it'd fall through to a literal, always-failing content search
            // for the text "key:BRD-40" instead).
            var spaceKeyUpper = match.Groups[1].Value.ToUpperInvariant();

            var filtered = query.Where(x =>
                x.Status!.Epic!.Space!.Key == spaceKeyUpper &&
                x.IssueNumber!.Number == issueNumber);

            return Task.FromResult<TokenResolution>(new AppliedResolution(filtered));
        }

        if (isFinalized)
        {
            // Finalized (trailing space, or another word came after) but still doesn't match
            // the expected shape — this is a genuine mistake, worth telling the user about.
            return Task.FromResult<TokenResolution>(new ErrorResolution(
                "Invalid issue key",
                "Use a format like key:BRD-40."));
        }

        // Still typing and not yet a complete key ("key:BR", "key:BRD-") — nothing useful to
        // show yet, so fall back to free text rather than erroring prematurely mid-keystroke.
        return Task.FromResult<TokenResolution>(new SuggestionsResolution([]));
    }
}