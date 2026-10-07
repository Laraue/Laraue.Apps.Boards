namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// Materialized count of issues an organization created in one period of its plan - a bucketed counter
/// (one row per organization+period) rather than a single mutable "current period" row, so a new
/// period just means a new row instead of needing an explicit reset step. The period is Billing's: the
/// rolling month of a Free plan, the calendar month of a paid one; <see cref="PeriodStartedAt"/> is the
/// start Billing reports. Kept in sync with issue creation via an atomic upsert (see
/// <c>CoreIssuesService.Create</c>) rather than derived by counting <see cref="Issue"/> rows on every
/// read - <c>UsageLimitService</c> needs this on the issue-creation hot path.
/// </summary>
public class IssuePeriodCount
{
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    /// <summary>
    /// UTC start of the period, exactly as Billing returns it.
    /// </summary>
    public DateTime PeriodStartedAt { get; set; }

    public int Count { get; set; }
}
