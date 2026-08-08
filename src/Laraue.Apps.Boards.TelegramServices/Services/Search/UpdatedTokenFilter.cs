using System.Text.RegularExpressions;
using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Handles "upd:&lt;op&gt;N&lt;unit&gt;" where op is one of &gt;, &gt;=, &lt;, &lt;=, = (or
/// omitted / "-", both aliases for &lt;=/&gt;= depending on read — see below) and unit is
/// d(ays)/h(ours)/w(eeks). The operator constrains the *age* of the last update (now minus
/// UpdatedAt), not the raw timestamp directly — "upd:&lt;6d" means "last updated less than 6
/// days ago" (recent), "upd:&gt;6d" means "last updated more than 6 days ago" (stale). Since
/// age runs opposite to the timestamp axis (a smaller age is a *larger*, more recent
/// UpdatedAt), each operator maps to the flipped comparison against the anchor
/// ("N units ago"): age &lt; N ⟺ UpdatedAt &gt; anchor, age &gt; N ⟺ UpdatedAt &lt; anchor, etc.
/// No suggestions are offered while typing since this is a free-form value, not something
/// to pick from a list.
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
            var op = match.Groups["op"].Success ? match.Groups["op"].Value : "<=";
            var num = int.Parse(match.Groups["num"].Value);
            var unit = match.Groups["unit"].Value.ToLowerInvariant();

            var span = unit switch
            {
                "h" => TimeSpan.FromHours(num),
                "d" => TimeSpan.FromDays(num),
                "w" => TimeSpan.FromDays(num * 7),
                _ => TimeSpan.Zero
            };

            // The anchor is "N units ago". The operator constrains *age* (now - UpdatedAt),
            // which runs opposite to the raw timestamp: a smaller age means a larger
            // (more recent) UpdatedAt, so age-comparisons flip when translated to UpdatedAt.
            var anchor = DateTime.UtcNow - span;

            IQueryable<Issue> filtered = op switch
            {
                "<" => query.Where(x => x.UpdatedAt > anchor),          // age < N  → recent
                "<=" or "-" => query.Where(x => x.UpdatedAt >= anchor), // age <= N → recent (inclusive)
                ">" => query.Where(x => x.UpdatedAt < anchor),          // age > N  → stale
                ">=" => query.Where(x => x.UpdatedAt <= anchor),        // age >= N → stale (inclusive)
                "=" => ApplyEquals(query, anchor, unit),                // age == N (± bucket width)
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
                "Use a format like upd:<6d (updated less than 6 days ago), upd:>6d (more than " +
                "6 days ago), upd:<=6d, upd:>=6d, upd:=6d, upd:6d or upd:12h."));
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