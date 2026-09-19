namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// Materialized count of issues an organization created in one UTC calendar month - a bucketed
/// counter (one row per organization+month) rather than a single mutable "current month" row, so
/// a new month just means a new row instead of needing an explicit reset step. Kept in sync with
/// issue creation via an atomic upsert (see <c>CoreIssuesService.Create</c>) rather than derived
/// by counting <see cref="Issue"/> rows on every read - <c>UsageLimitService</c> needs this on the
/// issue-creation hot path.
/// </summary>
public class IssueMonthlyCount
{
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public int Year { get; set; }
    public int Month { get; set; }

    public int Count { get; set; }
}
