using System.Text.RegularExpressions;
using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Handles "upd:&gt;7d", "upd:-7d", "upd:3w", "upd:12h" — all mean "updated within the last
/// N units". No suggestions are offered while typing since this is a free-form numeric value,
/// not something to pick from a list.
/// </summary>
public sealed class UpdatedTokenFilter : IQueryTokenFilter
{
    private static readonly Regex ValueRegex = new(
        @"^(?<op>[>-])?(?<num>\d+)(?<unit>[dhw])$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Key => "upd";

    public Task<TokenResolution> ResolveAsync(
        FilterContext context,
        IQueryable<Issue> query,
        string value,
        bool isFinalized,
        CancellationToken ct)
    {
        var match = ValueRegex.Match(value);

        if (match.Success)
        {
            // The value already has the complete "[>-]N[dhw]" shape — resolve immediately,
            // even without a trailing space. Waiting for isFinalized would make an
            // already-complete filter silently do nothing until the user adds a space or
            // another word (it'd fall through to a literal, always-failing content search
            // for the text "upd:3w" instead) — same issue the key: filter had.
            var num = int.Parse(match.Groups["num"].Value);
            var unit = match.Groups["unit"].Value.ToLowerInvariant();

            var span = unit switch
            {
                "h" => TimeSpan.FromHours(num),
                "d" => TimeSpan.FromDays(num),
                "w" => TimeSpan.FromDays(num * 7),
                _ => TimeSpan.Zero
            };

            var updatedAfter = DateTime.UtcNow - span;
            var filtered = query.Where(x => x.UpdatedAt >= updatedAfter);
            return Task.FromResult<TokenResolution>(new AppliedResolution(filtered));
        }

        if (isFinalized)
        {
            // Finalized (trailing space, or another word came after) but still doesn't match
            // the expected shape — this is a genuine mistake, worth telling the user about.
            return Task.FromResult<TokenResolution>(new ErrorResolution(
                "Invalid updated filter",
                "Use a format like upd:>7d, upd:-7d, upd:3w or upd:12h."));
        }

        // Still typing and not yet a complete filter ("upd:", "upd:7") — nothing useful to
        // show yet, so fall back to free text rather than erroring prematurely mid-keystroke.
        return Task.FromResult<TokenResolution>(new SuggestionsResolution([]));
    }
}