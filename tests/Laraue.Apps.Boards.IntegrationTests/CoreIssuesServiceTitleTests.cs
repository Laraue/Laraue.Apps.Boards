using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class CoreIssuesServiceTitleTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_ShouldStoreGivenTitle_Always(bool isTitleSetExplicitly)
    {
        using var testScope = host.CreateTestScope();
        var (userId, organization) = await Init(testScope);

        var issue = await CreateIssue(testScope, organization, userId, "  My   title ", isTitleSetExplicitly, request => request
            .SetContent("# First line\nbody"));

        Assert.Equal("My title", issue.Title);
        Assert.Equal(isTitleSetExplicitly, issue.IsTitleSetExplicitly);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldRequireTitle_WhenTitleIsBlank(string title)
    {
        Assert.Throws<ArgumentException>(() => new IssueCreateRequest(1, DateTime.UtcNow, title, isTitleSetExplicitly: true));
    }

    [Fact]
    public async Task Update_ShouldNotChangeTitle_WhenContentChangesWithoutTitle()
    {
        using var testScope = host.CreateTestScope();
        var (userId, organization) = await Init(testScope);
        var issue = await CreateIssue(testScope, organization, userId, "Old title", false, request => request.SetContent("Old title"));

        await Update(testScope, issue.Id, userId, request => request.SetContent("New first line"));

        var updated = await Reload(testScope, issue.Id);
        Assert.Equal("Old title", updated.Title);
    }

    [Fact]
    public async Task Update_ShouldApplySuggestedTitle_WhenTitleWasNotSetExplicitly()
    {
        using var testScope = host.CreateTestScope();
        var (userId, organization) = await Init(testScope);
        var issue = await CreateIssue(testScope, organization, userId, "Old title", false, request => request.SetContent("Old title"));

        await Update(testScope, issue.Id, userId, request => request
            .SetContent("New first line")
            .SetSuggestedTitle("New first line"));

        var updated = await Reload(testScope, issue.Id);
        Assert.Equal("New first line", updated.Title);
        Assert.False(updated.IsTitleSetExplicitly);
        Assert.False(await testScope.Database.OrganizationLogItems
            .AnyAsync(x => x.PropertyType == PropertyType.Title));
    }

    [Fact]
    public async Task Update_ShouldIgnoreSuggestedTitle_WhenTitleWasSetExplicitly()
    {
        using var testScope = host.CreateTestScope();
        var (userId, organization) = await Init(testScope);
        var issue = await CreateIssue(testScope, organization, userId, "Mine", true, request => request.SetContent("Old"));

        await Update(testScope, issue.Id, userId, request => request
            .SetContent("New")
            .SetSuggestedTitle("Suggested"));

        var updated = await Reload(testScope, issue.Id);
        Assert.Equal("Mine", updated.Title);
        Assert.True(updated.IsTitleSetExplicitly);
    }

    [Fact]
    public async Task Update_ShouldSetExplicitTitleAndLogIt_WhenTitleIsChanged()
    {
        using var testScope = host.CreateTestScope();
        var (userId, organization) = await Init(testScope);
        var issue = await CreateIssue(testScope, organization, userId, "First line", false, request => request.SetContent("First line"));

        await Update(testScope, issue.Id, userId, request => request.SetTitle("Mine"));

        var updated = await Reload(testScope, issue.Id);
        Assert.Equal("Mine", updated.Title);
        Assert.True(updated.IsTitleSetExplicitly);

        var logItem = await testScope.Database.OrganizationLogItems
            .AsNoTracking()
            .SingleAsync(x => x.PropertyType == PropertyType.Title);
        Assert.Equal("First line", logItem.OldDisplayValue);
        Assert.Equal("Mine", logItem.NewDisplayValue);
    }

    [Fact]
    public async Task Update_ShouldNotPinTitleOrLogIt_WhenTheSameTitleIsResent()
    {
        using var testScope = host.CreateTestScope();
        var (userId, organization) = await Init(testScope);
        var issue = await CreateIssue(testScope, organization, userId, "First line", false, request => request.SetContent("First line"));

        await Update(testScope, issue.Id, userId, request => request.SetContent("Edited on the web").SetTitle("First line"));

        var updated = await Reload(testScope, issue.Id);
        Assert.Equal("First line", updated.Title);
        Assert.False(updated.IsTitleSetExplicitly);
        Assert.False(await testScope.Database.OrganizationLogItems
            .AnyAsync(x => x.PropertyType == PropertyType.Title));
    }

    private static async Task<(Guid UserId, Organization Organization)> Init(WebApiTestHostScope testScope)
    {
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        return (userId, organization);
    }

    private static async Task<Issue> CreateIssue(
        WebApiTestHostScope testScope,
        Organization organization,
        Guid userId,
        string title,
        bool isTitleSetExplicitly,
        Action<IssueCreateRequest> setup)
    {
        var request = new IssueCreateRequest(organization.GetStatus(0, 0, 0).Id, DateTime.UtcNow, title, isTitleSetExplicitly);
        setup(request);

        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();
        var issueId = await testScope.Services.GetRequiredService<ICoreIssuesService>()
            .Create(new Actor(userId, null), request, CancellationToken.None);
        await transaction.CommitAsync();

        return await Reload(testScope, issueId);
    }

    private static async Task Update(
        WebApiTestHostScope testScope,
        long issueId,
        Guid userId,
        Action<IssueUpdateRequest> setup)
    {
        var request = new IssueUpdateRequest();
        setup(request);

        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();
        await testScope.Services.GetRequiredService<ICoreIssuesService>()
            .Update(issueId, new Actor(userId, null), request, CancellationToken.None);
        await transaction.CommitAsync();
    }

    private static Task<Issue> Reload(WebApiTestHostScope testScope, long issueId)
    {
        return testScope.Database.Issues.AsNoTracking().SingleAsync(x => x.Id == issueId);
    }
}
