using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.DataAccess;

/// <summary>
/// Explicit "active rows only" query roots for the soft-deletable content hierarchy
/// (Organization/Space/Epic/Status/Issue/IssueComment). Prefer these over the raw <see cref="DatabaseContext"/>
/// DbSets for normal reads - use the raw DbSets only for audit/history features that must see
/// through soft-deletion (e.g. <c>OrganizationHistoryService</c>).
/// </summary>
public static class DatabaseContextActiveEntityExtensions
{
    public static IQueryable<Issue> ActiveIssues(this DatabaseContext context) =>
        context.Issues.Where(x => x.DeletedAt == null);
}
