using Laraue.Apps.Boards.DataAccess.Migrations;
using Laraue.Apps.Boards.DataAccess.Models;
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
    [InlineData("Fix the login", "Fix the login", null)]
    [InlineData("\n\n  Fix the login\nsecond line", "Fix the login", "\n\n  Fix the login\nsecond line")]
    [InlineData("> - 1. Fix   the\tlogin", "Fix the login", "> - 1. Fix   the\tlogin")]
    [InlineData("- [Fix the login](https://example.com/x) now", "Fix the login now", "- [Fix the login](https://example.com/x) now")]
    [InlineData("_Fix_ snake_case", "Fix snake_case", "_Fix_ snake_case")]
    [InlineData("#hashtag is not a heading", "#hashtag is not a heading", null)]
    [InlineData("Fix the login. Then check the logs.\nSecond line", "Fix the login", "Fix the login. Then check the logs.\nSecond line")]
    [InlineData("Release 1.2 of example.com is out", "Release 1.2 of example.com is out", null)]
    [InlineData("Fix the login.", "Fix the login.", null)]
    [InlineData("**Fix the login.**   *Then* more", "Fix the login", "**Fix the login.**   *Then* more")]
    [InlineData("# Fix the login. Then more\nBody", "Fix the login", "# Fix the login. Then more\nBody")]
    [InlineData("Title one. Title two\n---\nBody", "Title one", "Title one. Title two\n---\nBody")]
    [InlineData("", "", "")]
    [InlineData("  Fix the login  \n", "Fix the login", null)]
    [InlineData("**Fix the login**", "Fix the login", "**Fix the login**")]
    public async Task Backfill_ShouldSanitizeFirstLineAndKeepContent_WhenFirstLineIsNotAMarkdownTitle(
        string content,
        string expectedTitle,
        string? expectedContent)
    {
        var (title, newContent) = await RunBackfillOnOneIssue(content);

        Assert.Equal(expectedTitle, title);
        Assert.Equal(expectedContent ?? string.Empty, newContent ?? string.Empty);
    }

    [Theory]
    [InlineData("# Fix the login\nBody line one\nBody line two", "Fix the login", "Body line one\nBody line two")]
    [InlineData("\n  ## **Fix** the `login`  \n\n\nBody", "Fix the login", "Body")]
    [InlineData("# Only a heading", "Only a heading", null)]
    [InlineData("Title line\n---\nBody line", "Title line", "Body line")]
    [InlineData("Title line\n---\n\n- a\n---\n- b", "Title line", "- a\n---\n- b")]
    [InlineData("Title line\n---", "Title line", null)]
    [InlineData("Title line\n\n---\nBody", "Title line", "Body")]
    [InlineData(
        "Support Page Example\n\n---\n\nSupport page example. We can make something similar without a backend.",
        "Support Page Example",
        "Support page example. We can make something similar without a backend.")]
    [InlineData("Title line\nBody line\n\n---\nOther", "Title line", "Title line\nBody line\n\n---\nOther")]
    [InlineData("---\nFix the login\nmore", "Fix the login", "Fix the login\nmore")]
    [InlineData("\n---\n\n---\n# Heading\nBody", "Heading", "Body")]
    [InlineData("---", "", null)]
    [InlineData("# Title\n---\nBody", "Title", "Body")]
    [InlineData("Title line\n---\n---\nBody", "Title line", "Body")]
    [InlineData("Title line\n---\n\n---\nBody", "Title line", "Body")]
    public async Task Backfill_ShouldMoveMarkdownTitleOutOfContent_WhenFirstLineIsAHeadingOrIsFollowedByARule(
        string content,
        string expectedTitle,
        string? expectedContent)
    {
        var (title, newContent) = await RunBackfillOnOneIssue(content);

        Assert.Equal(expectedTitle, title);
        Assert.Equal(expectedContent, newContent);
    }

    [Theory]
    [InlineData("Title\n---\nBody one", "Title\n---\nBody two", PropertyType.Content, "Body one", "Body two")]
    [InlineData("Old title\n---\nBody", "New title\n---\nBody", PropertyType.Title, "Old title", "New title")]
    [InlineData("Fix the login", "Fix the login now", PropertyType.Title, "Fix the login", "Fix the login now")]
    [InlineData("First line\nsecond", "First line\nsecond edited", PropertyType.Content, "First line\nsecond", "First line\nsecond edited")]
    [InlineData(null, "# Title\nBody", PropertyType.Content, null, "Body")]
    [InlineData(null, "Support Page Example\n\n---\n\nSupport page example. Without a backend.", PropertyType.Content, null, "Support page example. Without a backend.")]
    [InlineData(null, "Fix the login", null, null, null)]
    public async Task Backfill_ShouldKeepIssueDescriptionHistoryConsistent_WithTheBackfilledDescriptions(
        string? oldValue,
        string newValue,
        PropertyType? expectedType,
        string? expectedOld,
        string? expectedNew)
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, org => org
            .AddIssueToDefaultStatus(userId, builder => builder.WithContent("Whatever")));
        var issueId = organization.GetIssueData(0, 0, 0, 0).Issue.Id;
        var logId = await AddContentLog(testScope, organization.Id, userId, issueId, LogEntityType.Issue, oldValue, newValue);

        var items = await RunBackfillOnHistory(testScope, logId);

        if (expectedType is null)
        {
            Assert.Empty(items);
            return;
        }

        var item = Assert.Single(items);
        Assert.Equal(expectedType, item.PropertyType);
        Assert.Equal(expectedOld, item.OldDisplayValue);
        Assert.Equal(expectedNew, item.NewDisplayValue);
    }

    [Fact]
    public async Task Backfill_ShouldNotTouchCommentHistory_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, org => org
            .AddIssueToDefaultStatus(userId, builder => builder.WithContent("Whatever")));
        var issueId = organization.GetIssueData(0, 0, 0, 0).Issue.Id;
        var logId = await AddContentLog(
            testScope, organization.Id, userId, issueId, LogEntityType.Comment, null, "# Looks like a title\nbut it is a comment");

        var items = await RunBackfillOnHistory(testScope, logId);

        var item = Assert.Single(items);
        Assert.Equal(PropertyType.Content, item.PropertyType);
        Assert.Equal("# Looks like a title\nbut it is a comment", item.NewDisplayValue);
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
        Assert.Equal(new string('a', 400), titles[issue.Id].Content);
    }

    private static async Task<long> AddContentLog(
        WebApiTestHostScope testScope,
        long organizationId,
        Guid userId,
        long entityId,
        LogEntityType entityType,
        string? oldValue,
        string newValue)
    {
        var log = new OrganizationLog
        {
            EntityId = entityId,
            EntityType = entityType,
            OrganizationId = organizationId,
            OwnerId = userId,
            CreatedAt = DateTime.UtcNow,
            Action = oldValue is null ? LogAction.Create : LogAction.Update,
            Items =
            [
                new OrganizationLogItem
                {
                    PropertyType = PropertyType.Content,
                    OldDisplayValue = oldValue,
                    NewDisplayValue = newValue,
                },
            ],
        };
        testScope.Database.Add(log);
        await testScope.Database.SaveChangesAsync();

        return log.Id;
    }

    private static async Task<List<OrganizationLogItem>> RunBackfillOnHistory(WebApiTestHostScope testScope, long logId)
    {
        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();

        await testScope.Database.Database.ExecuteSqlRawAsync(AddIssueTitle.BackfillTitleSql);

        var items = await testScope.Database.OrganizationLogItems
            .AsNoTracking()
            .Where(x => x.OrganizationLogId == logId)
            .ToListAsync();

        await transaction.RollbackAsync();

        return items;
    }

    private async Task<(string Title, string? Content)> RunBackfillOnOneIssue(string content)
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, org => org
            .AddIssueToDefaultStatus(userId, builder => builder.WithContent(content)));
        var issue = organization.GetIssueData(0, 0, 0, 0).Issue;

        var result = await RunBackfill(testScope, issue.Id);

        return result[issue.Id];
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
