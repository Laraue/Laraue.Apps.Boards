using System.Text.RegularExpressions;
using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Handles "upd:&lt;op&gt;N&lt;unit&gt;" where op is one of &gt;, &gt;=, &lt;, &lt;=, = (or
/// omitted / "-", both aliases for &gt;= — kept for backward compatibility) and unit is
/// d(ays)/h(ours)/w(eeks). Examples: "upd:&gt;7d" (updated more recently than 7 days ago),
/// "upd:&lt;7d" (last updated more than 7 days ago, i.e. stale), "upd:=7d" (updated on
/// exactly that day/hour, N units ago). No suggestions are offered while typing since this
/// is a free-form value, not something to pick from a list.
/// </summary>
public sealed class UpdatedTokenFilter : IQueryTokenFilter
{
    private static readonly Regex ValueRegex = new(
        @"^(?<op>>=|<=|>|<|=|-)?(?<num>\d+)(?<unit>[dhw])$",
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
            // The value already has the complete "[op]N[dhw]" shape — resolve immediately,
            // even without a trailing space. Waiting for isFinalized would make an
            // already-complete filter silently do nothing until the user adds a space or
            // another word (it'd fall through to a literal, always-failing content search
            // for the text "upd:3w" instead) — same issue the key: filter had.
            var op = match.Groups["op"].Success ? match.Groups["op"].Value : ">=";
            var num = int.Parse(match.Groups["num"].Value);
            var unit = match.Groups["unit"].Value.ToLowerInvariant();

            var span = unit switch
            {
                "h" => TimeSpan.FromHours(num),
                "d" => TimeSpan.FromDays(num),
                "w" => TimeSpan.FromDays(num * 7),
                _ => TimeSpan.Zero
            };

            // The anchor is "N units ago" — every operator compares UpdatedAt against this
            // single point. ">"/">=" mean "more recently than N units ago" (i.e. within the
            // last N units); "<"/"<=" mean "further back than N units ago" (i.e. stale,
            // hasn't been touched in at least N units).
            var anchor = DateTime.UtcNow - span;

            IQueryable<Issue> filtered = op switch
            {
                ">" => query.Where(x => x.UpdatedAt > anchor),
                ">=" or "-" => query.Where(x => x.UpdatedAt >= anchor),
                "<" => query.Where(x => x.UpdatedAt < anchor),
                "<=" => query.Where(x => x.UpdatedAt <= anchor),
                "=" => ApplyEquals(query, anchor, unit),
                _ => query.Where(x => x.UpdatedAt >= anchor)
            };

            return Task.FromResult<TokenResolution>(new AppliedResolution(filtered));
        }

        if (isFinalized)
        {
            // Finalized (trailing space, or another word came after) but still doesn't match
            // the expected shape — this is a genuine mistake, worth telling the user about.
            return Task.FromResult<TokenResolution>(new ErrorResolution(
                "Invalid updated filter",
                "Use a format like upd:>7d, upd:<7d, upd:>=7d, upd:<=7d, upd:=7d, upd:3w or upd:12h."));
        }

        // Still typing and not yet a complete filter ("upd:", "upd:7") — nothing useful to
        // show yet, so fall back to free text rather than erroring prematurely mid-keystroke.
        return Task.FromResult<TokenResolution>(new SuggestionsResolution([]));
    }

    /// <summary>
    /// "upd:=Nd" means "updated on that exact calendar day"; "upd:=Nh" means "updated within
    /// that exact hour". An exact-instant match against a timestamp would never realistically
    /// hit, so "=" is treated as a bucket the width of one unit, aligned to the anchor.
    /// </summary>
    private static IQueryable<Issue> ApplyEquals(IQueryable<Issue> query, DateTime anchor, string unit)
    {
        DateTime start, end;

        if (unit == "h")
        {
            start = new DateTime(anchor.Year, anchor.Month, anchor.Day, anchor.Hour, 0, 0, DateTimeKind.Utc);
            end = start.AddHours(1);
        }
        else
        {
            start = DateTime.SpecifyKind(anchor.Date, DateTimeKind.Utc);
            end = start.AddDays(1);
        }

        return query.Where(x => x.UpdatedAt >= start && x.UpdatedAt < end);
    }
}