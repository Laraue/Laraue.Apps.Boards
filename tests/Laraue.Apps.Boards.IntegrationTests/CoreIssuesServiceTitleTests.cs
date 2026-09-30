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
    [Fact]
    public async Task Create_ShouldDeriveTitleFromFirstLine_WhenTitleIsNotSet()
    {
        using var testScope = host.CreateTestScope();
        var (userId, organization) = await Init(testScope);

        var issue = await CreateIssue(testScope, organization, userId, request => request
            .SetContent("\n## Fix the *login*\nDetails"));

        Assert.Equal("Fix the login", issue.Title);
        Assert.False(issue.IsTitleSetExplicitly);
    }

    [Fact]
    public async Task Create_ShouldUseSuggestedTitle_WhenAiProvidedOne()
    {
        using var testScope = host.CreateTestScope();
        var (userId, organization) = await Init(testScope);

        var issue = await CreateIssue(testScope, organization, userId, request => request
            .SetContent("- Login fails on retry")
            .SetSuggestedTitle("Fix login retry"));

        Assert.Equal("Fix login retry", issue.Title);
        Assert.False(issue.IsTitleSetExplicitly);
    }

    [Fact]
    public async Task Create_ShouldUseExplicitTitle_WhenTitleIsSetAlongWithSuggestion()
    {
        using var testScope = host.CreateTestScope();
        var (userId, organization) = await Init(testScope);

        var issue = await CreateIssue(testScope, organization, userId, request => request
            .SetContent("Some content")
            .SetSuggestedTitle("Suggested")
            .SetTitle("  My   title "));

        Assert.Equal("My title", issue.Title);
        Assert.True(issue.IsTitleSetExplicitly);
    }

    [Fact]
    public async Task Update_ShouldFollowFirstLine_WhenTitleWasNeverSetExplicitly()
    {
        using var testScope = host.CreateTestScope();
        var (userId, organization) = await Init(testScope);
        var issue = await CreateIssue(testScope, organization, userId, request => request.SetContent("Old title"));

        await Update(testScope, issue.Id, userId, request => request.SetContent("New title\nbody"));

        var updated = await Reload(testScope, issue.Id);
        Assert.Equal("New title", updated.Title);
        Assert.False(updated.IsTitleSetExplicitly);
    }

    [Fact]
    public async Task Update_ShouldKeepTitle_WhenTitleWasSetExplicitlyAndContentChanges()
    {
        using var testScope = host.CreateTestScope();
        var (userId, organization) = await Init(testScope);
        var issue = await CreateIssue(testScope, organization, userId, request => request
            .SetContent("Old title")
            .SetTitle("Mine"));

        await Update(testScope, issue.Id, userId, request => request
            .SetContent("New title")
            .SetSuggestedTitle("Suggested"));

        var updated = await Reload(testScope, issue.Id);
        Assert.Equal("Mine", updated.Title);
        Assert.True(updated.IsTitleSetExplicitly);
    }

    [Fact]
    public async Task Update_ShouldDeriveTitleAgain_WhenExplicitTitleIsCleared()
    {
        using var testScope = host.CreateTestScope();
        var (userId, organization) = await Init(testScope);
        var issue = await CreateIssue(testScope, organization, userId, request => request
            .SetContent("First line")
            .SetTitle("Mine"));

        await Update(testScope, issue.Id, userId, request => request.SetTitle(""));

        var updated = await Reload(testScope, issue.Id);
        Assert.Equal("First line", updated.Title);
        Assert.False(updated.IsTitleSetExplicitly);
    }

    [Fact]
    public async Task Update_ShouldSetExplicitTitleAndLogIt_WhenTitleIsChanged()
    {
        using var testScope = host.CreateTestScope();
        var (userId, organization) = await Init(testScope);
        var issue = await CreateIssue(testScope, organization, userId, request => request.SetContent("First line"));

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
    public async Task Update_ShouldNotLogTitle_WhenTitleFollowsContent()
    {
        using var testScope = host.CreateTestScope();
        var (userId, organization) = await Init(testScope);
        var issue = await CreateIssue(testScope, organization, userId, request => request.SetContent("Old"));

        await Update(testScope, issue.Id, userId, request => request.SetContent("New"));

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
        Action<IssueCreateRequest> setup)
    {
        var request = new IssueCreateRequest(organization.GetStatus(0, 0, 0).Id, DateTime.UtcNow);
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
