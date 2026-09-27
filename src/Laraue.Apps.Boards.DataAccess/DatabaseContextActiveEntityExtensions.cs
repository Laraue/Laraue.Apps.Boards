using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.DataAccess;

/// <summary>
/// Explicit "active rows only" query roots for the soft-deletable content hierarchy
/// (Organization/Space/Epic/Status/Issue/IssueComment) and users. Prefer these over the raw <see cref="DatabaseContext"/>
/// DbSets for normal reads - use the raw DbSets only for audit/history features that must see
/// through soft-deletion (e.g. <c>OrganizationHistoryService</c>).
/// </summary>
public static class DatabaseContextActiveEntityExtensions
{
    public static IQueryable<Issue> ActiveIssues(this DatabaseContext context) =>
        context.Issues.Where(x => x.DeletedAt == null);

    public static IQueryable<Space> ActiveSpaces(this DatabaseContext context) =>
        context.Spaces.Where(x => x.DeletedAt == null);

    public static IQueryable<Epic> ActiveEpics(this DatabaseContext context) =>
        context.Epics.Where(x => x.DeletedAt == null);

    public static IQueryable<Status> ActiveStatuses(this DatabaseContext context) =>
        context.Statuses.Where(x => x.DeletedAt == null);

    public static IQueryable<Organization> ActiveOrganizations(this DatabaseContext context) =>
        context.Organizations.Where(x => x.DeletedAt == null);

    public static IQueryable<IssueComment> ActiveIssueComments(this DatabaseContext context) =>
        context.IssueComments.Where(x => x.DeletedAt == null);

    public static IQueryable<User> ActiveUsers(this DatabaseContext context) =>
        context.Users.Where(x => x.DeletedAt == null);

    /// <summary>
    /// Current members only - see <see cref="OrganizationUser.LeftAt"/>. Use the raw
    /// <see cref="DatabaseContext.OrganizationUsers"/> only to show a (possibly former) member's name.
    /// </summary>
    public static IQueryable<OrganizationUser> ActiveOrganizationUsers(this DatabaseContext context) =>
        context.OrganizationUsers.Where(x => x.LeftAt == null);
}
