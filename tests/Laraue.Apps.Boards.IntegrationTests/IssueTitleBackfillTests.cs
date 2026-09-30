using Laraue.Apps.Boards.DataAccess.Migrations;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Runs <see cref="AddIssueTitle"/>'s backfill on seeded issues - the migration itself ran on an
/// empty test database. The backfill touches every issue, so it runs in a transaction that is rolled
/// back, leaving the other tests' data as it was.
/// </summary>
[Collection("IntegrationTest")]
public class IssueTitleBackfillTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Theory]
    [InlineData("Fix the login", "Fix the login")]
    [InlineData("\n\n  Fix the login\nsecond line", "Fix the login")]
    [InlineData("# **Fix** the `login`", "Fix the login")]
    [InlineData("> - 1. Fix   the\tlogin", "Fix the login")]
    [InlineData("- [Fix the login](https://example.com/x) now", "Fix the login now")]
    [InlineData("_Fix_ snake_case", "Fix snake_case")]
    [InlineData("", "")]
    public async Task Backfill_ShouldSanitizeFirstLine_WhenIssueHasContent(string content, string expectedTitle)
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, org => org
            .AddIssueToDefaultStatus(userId, builder => builder.WithContent(content)));
        var issue = organization.GetIssueData(0, 0, 0, 0).Issue;

        var titles = await RunBackfill(testScope, issue.Id);

        Assert.Equal(expectedTitle, titles[issue.Id].Title);
        Assert.Equal(content, titles[issue.Id].Content ?? string.Empty);
    }

    [Fact]
    public async Task Backfill_ShouldCutTitleWithEllipsis_WhenFirstLineIsTooLong()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, org => org
            .AddIssueToDefaultStatus(userId, builder => builder.WithContent(new string('a', 400))));
        var issue = organization.GetIssueData(0, 0, 0, 0).Issue;

        var titles = await RunBackfill(testScope, issue.Id);

        Assert.Equal(new string('a', 255) + "…", titles[issue.Id].Title);
    }

    private static async Task<Dictionary<long, (string Title, string? Content)>> RunBackfill(
        WebApiTestHostScope testScope,
        params long[] issueIds)
    {
        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();

        await testScope.Database.Database.ExecuteSqlRawAsync(AddIssueTitle.BackfillTitleSql);

        var rows = await testScope.Database.Issues
            .AsNoTracking()
            .Where(x => issueIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Title, x.Content })
            .ToListAsync();

        await transaction.RollbackAsync();

        return rows.ToDictionary(x => x.Id, x => (x.Title, x.Content));
    }
}
