using System.Text.RegularExpressions;
using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Handles "upd:&lt;op&gt;N&lt;unit&gt;" (op: &gt; &gt;= &lt; &lt;= = or bare/"-", unit:
/// d/h/w). Same no-candidate-list adaptation as <see cref="IssueKeyTokenFilter"/>: a complete
/// shape is inherently unambiguous, so it applies immediately (with a human-readable computed
/// preview shown via <see cref="AppliedResolution.Description"/> if the search ends up empty)
/// with no marker needed. No wildcard concept here — "starts with" doesn't apply to a duration.
///
/// The operator constrains the *age* of the last update (now - UpdatedAt), which runs
/// opposite to the raw timestamp: "upd:&lt;6d" means "last updated less than 6 days ago"
/// (recent) -&gt; UpdatedAt &gt; anchor. See ComputeAnchor for the full mapping.
/// </summary>
public sealed class UpdatedTokenFilter : IQueryTokenFilter
{
    // Still typing, not yet complete: an optional operator, and zero or more digits, no unit.
    private static readonly Regex StillValidPrefixRegex = new(
        @"^(>=|<=|>|<|=|-)?\d*$", RegexOptions.Compiled);

    private static readonly Regex CompleteRegex = new(
        @"^(?<op>>=|<=|>|<|=|-)?(?<num>\d+)(?<unit>[dhw])$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Key => "upd";

    public Task<TokenResolution> ResolveAsync(
        FilterContext context,
        IQueryable<Issue> query,
        string value,
        bool isFollowedByAnotherToken,
        CancellationToken ct)
    {
        if (value.Length == 0)
        {
            return Task.FromResult<TokenResolution>(new PreviewResolution(
                "Type a duration",
                "e.g. >7d, <3w, =12h"));
        }

        var match = CompleteRegex.Match(value);
        if (match.Success)
        {
            var (anchor, description) = ComputeAnchor(match);
            var op = match.Groups["op"].Success ? match.Groups["op"].Value : "<=";
            var unit = match.Groups["unit"].Value.ToLowerInvariant();

            IQueryable<Issue> filtered = op switch
            {
                "<" => query.Where(x => x.UpdatedAt > anchor),          // age < N  -> recent
                "<=" or "-" => query.Where(x => x.UpdatedAt >= anchor), // age <= N -> recent (inclusive)
                ">" => query.Where(x => x.UpdatedAt < anchor),          // age > N  -> stale
                ">=" => query.Where(x => x.UpdatedAt <= anchor),        // age >= N -> stale (inclusive)
                "=" => ApplyEquals(query, anchor, unit),                // age == N (+/- bucket width)
                _ => query.Where(x => x.UpdatedAt >= anchor)
            };

            return Task.FromResult<TokenResolution>(new AppliedResolution(
                filtered, Description: description.ToLowerInvariant()));
        }

        if (StillValidPrefixRegex.IsMatch(value))
        {
            if (isFollowedByAnotherToken)
            {
                return Task.FromResult<TokenResolution>(new ErrorResolution(
                    "Incomplete updated filter",
                    $"\"{value}\" isn't a complete duration — expected e.g. >7d, <3w, =12h."));
            }

            return Task.FromResult<TokenResolution>(new PreviewResolution(
                "Type a duration",
                "e.g. >7d, <3w, =12h — keep typing"));
        }

        // Already broken (e.g. a letter that isn't a valid unit, or a second operator) — can
        // never become valid by typing more, so error now rather than waiting.
        return Task.FromResult<TokenResolution>(new ErrorResolution(
            "Invalid updated filter",
            $"\"{value}\" isn't valid — use a format like >7d, <3w, =12h."));
    }

    /// <summary>
    /// Computes the "N units ago" anchor and a human-readable description of what the filter
    /// actually does — used both as the applied-filter description and (implicitly, via the
    /// same logic) understandable even without a separate preview step, since it now applies
    /// immediately once complete.
    /// </summary>
    private static (DateTime Anchor, string Description) ComputeAnchor(Match match)
    {
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

        var anchor = DateTime.UtcNow - span;
        var anchorText = anchor.ToString("MMM d, yyyy");
        var unitWord = unit switch { "h" => "hour(s)", "d" => "day(s)", "w" => "week(s)", _ => unit };

        var description = op switch
        {
            "<" => $"Updated after {anchorText} (less than {num} {unitWord} ago)",
            "<=" or "-" => $"Updated within the last {num} {unitWord} (since {anchorText})",
            ">" => $"Updated before {anchorText} (more than {num} {unitWord} ago)",
            ">=" => $"Not updated in the last {num} {unitWord} (before {anchorText})",
            "=" => $"Updated on {anchorText}",
            _ => $"Updated relative to {anchorText}"
        };

        return (anchor, description);
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