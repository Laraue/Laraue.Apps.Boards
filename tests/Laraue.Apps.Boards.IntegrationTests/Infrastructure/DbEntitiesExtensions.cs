using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public static class DbEntitiesExtensions
{
    public static Task<Issue?> FindIssueByKey(this DatabaseContext dbContext, long organizationId, string issueKey)
    {
        var number = new IssueKey(issueKey);

        return dbContext.IssueNumbers
            .Where(n => n.Number == number.Number)
            .Where(n => n.Space!.OrganizationId == organizationId)
            .Where(n => n.Space!.Key == number.SpaceKey)
            .Select(n => n.Issue!)
            .FirstOrDefaultAsyncEF();
    }
}