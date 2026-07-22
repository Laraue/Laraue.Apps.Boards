using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public static class DbExtensions
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

    public static void CleanDatabase(this DatabaseContext dbContext)
    {
        dbContext.SpaceCounters.ExecuteDelete();
        dbContext.TelegramFiles.ExecuteDelete();
        dbContext.Issues.ExecuteDelete();
        dbContext.TelegramMessages.ExecuteDelete();
        dbContext.Attachments.ExecuteDelete();
        dbContext.Users.ExecuteDelete();
        dbContext.TelegramMediaGroups.ExecuteDelete();
    }
}