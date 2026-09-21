using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.McpHost.Services;
using Laraue.Apps.Boards.Services;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Exercises <see cref="IssueMcpService"/> directly, bypassing the actual MCP HTTP/SSE transport -
/// there's no permission/mutation logic here that isn't already covered by
/// <see cref="IAccessService"/>/<c>ICoreIssuesService</c> themselves, so this only needs to confirm
/// the service wires the caller's identity and those checks together correctly.
/// <see cref="Laraue.Apps.Boards.McpHost.Tools.IssueTools"/> itself is a thin MCP adapter with
/// nothing left to test beyond "does it call this service" - the same reason a controller doesn't
/// usually get its own dedicated tests separate from the service it calls into.
/// </summary>
[Collection("IntegrationTest")]
public class IssueMcpServiceTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private static IIssueMcpService CreateIssueMcpService(WebApiTestHostScope testScope)
    {
        return new IssueMcpService(
            testScope.Database,
            testScope.Services.GetRequiredService<IAccessService>(),
            testScope.Services.GetRequiredService<ICoreIssuesService>(),
            testScope.Services.GetRequiredService<ICoreSpacesService>(),
            testScope.Services.GetRequiredService<IDateTimeProvider>());
    }

    private static OrganizationAuthData AuthDataFor(long organizationId, Guid userId)
    {
        return new OrganizationAuthData { OrganizationId = organizationId, UserId = userId };
    }

    [Fact]
    public async Task ListIssues_ShouldOnlyReturnAccessibleIssues_WhenCalled()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId)
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var expectedStatus = organization.GetStatus(0, 0, 0);
        var ownerDisplayName = await testScope.Database.Users
            .Where(x => x.Id == ownerId)
            .Select(x => x.DisplayName)
            .SingleAsyncEF();

        var issueMcpService = CreateIssueMcpService(testScope);

        var ownerIssues = await issueMcpService.ListIssues(
            AuthDataFor(organization.Id, ownerId), null, null, null, CancellationToken.None);
        var memberIssues = await issueMcpService.ListIssues(
            AuthDataFor(organization.Id, memberId), null, null, null, CancellationToken.None);

        var issue = Assert.Single(ownerIssues);
        Assert.Equal(issueData.Key, issue.Key);
        Assert.Equal("Fix the thing", issue.Title);
        Assert.Equal(expectedStatus.Name, issue.Status);
        Assert.Equal(ownerDisplayName, issue.Assignee);
        Assert.Empty(memberIssues);
    }

    [Fact]
    public async Task ListIssues_ShouldApplyFilters_WhenGiven()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var otherAssignee = await testScope.CreateUser(u => u.TelegramUserName = "other_assignee");
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            // A space added via AddSpace auto-gets an implicit "Backlog" epic at index 0 and,
            // within every epic in it, an implicit default status at index 0 - so this test's own
            // explicit epic/status land at index 1.
            .AddSpace(ownerId, space => space
                .AddEpic(ownerId, epic => epic
                    .AddStatus(s => s.WithName("In Progress"))
                    .AddIssue(ownerId, 1, issue => issue.WithContent("Target"))
                    .AddIssue(ownerId, 0, issue => issue.WithContent("Wrong status"))))
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Wrong space")));

        var targetIssueData = organization.GetIssueData(1, 1, 1, 0);
        await testScope.Database.Issues
            .Where(x => x.Id == targetIssueData.Issue.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.AssigneeId, otherAssignee));

        var wrongStatusIssueKey = organization.GetIssueData(1, 1, 0, 0).Key;

        var issueMcpService = CreateIssueMcpService(testScope);
        var authData = AuthDataFor(organization.Id, ownerId);

        var bySpace = await issueMcpService.ListIssues(
            authData, organization.GetSpace(1).Key, null, null, CancellationToken.None);
        var byStatus = await issueMcpService.ListIssues(
            authData, null, "In Progress", null, CancellationToken.None);
        var byAssignee = await issueMcpService.ListIssues(
            authData, null, null, "other_assignee", CancellationToken.None);

        Assert.Equal(
            new HashSet<string> { targetIssueData.Key, wrongStatusIssueKey },
            bySpace.Select(x => x.Key).ToHashSet());
        Assert.Equal(targetIssueData.Key, Assert.Single(byStatus).Key);
        Assert.Equal(targetIssueData.Key, Assert.Single(byAssignee).Key);
    }

    [Fact]
    public async Task GetIssue_ShouldReturnContentAndComments_WhenAccessible()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue
                .WithContent("Fix the thing")
                .AddComment(ownerId, "First comment")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var expectedStatus = organization.GetStatus(0, 0, 0);
        var expectedComment = issueData.Issue.IssueComments!.Single();
        var ownerDisplayName = await testScope.Database.Users
            .Where(x => x.Id == ownerId)
            .Select(x => x.DisplayName)
            .SingleAsyncEF();

        var detail = await CreateIssueMcpService(testScope)
            .GetIssue(AuthDataFor(organization.Id, ownerId), issueData.Key, CancellationToken.None);

        Assert.Equal(issueData.Key, detail.Key);
        Assert.Equal("Fix the thing", detail.Content);
        Assert.Equal(expectedStatus.Name, detail.Status);
        Assert.Equal(ownerDisplayName, detail.Assignee);
        Assert.Equal(issueData.Issue.CreatedAt, detail.CreatedAt);
        Assert.Equal(issueData.Issue.UpdatedAt, detail.UpdatedAt);

        var comment = Assert.Single(detail.Comments);
        Assert.Equal(expectedComment.Id, comment.Id);
        Assert.Equal(ownerDisplayName, comment.Author);
        Assert.Equal("First comment", comment.Text);
        Assert.Equal(expectedComment.CreatedAt, comment.CreatedAt);
    }

    [Fact]
    public async Task MoveIssueStatus_ShouldMoveIssue_WhenCallerCanUpdateIssues()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, space => space
                .AddEpic(ownerId, epic => epic
                    .AddStatus(s => s.WithName("In Progress"))
                    .AddIssue(ownerId, 0, issue => issue.WithContent("Fix the thing")))));

        // A space added via AddSpace (unlike the org's own default space) auto-gets an implicit
        // "Backlog" epic at index 0 and, within every epic in it, an implicit default status at
        // index 0 - so this test's own explicit epic/status land at index 1, not 0.
        var issueData = organization.GetIssueData(1, 1, 0, 0);
        var targetStatus = organization.GetStatus(1, 1, 1);

        await CreateIssueMcpService(testScope).MoveIssueStatus(
            AuthDataFor(organization.Id, ownerId), issueData.Key, "In Progress", CancellationToken.None);

        var updatedIssue = await testScope.Database.Issues.SingleAsyncEF(x => x.Id == issueData.Issue.Id);
        Assert.Equal(targetStatus.Id, updatedIssue.StatusId);
    }

    [Fact]
    public async Task MoveIssueStatus_ShouldThrow_WhenCallerLacksUpdatePermission()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder.SetGlobalAccessLevel(x => x.CanRead = true))
            .AddSpace(ownerId, space => space
                .AddEpic(ownerId, epic => epic
                    .AddStatus(s => s.WithName("In Progress"))
                    .AddIssue(ownerId, 0, issue => issue.WithContent("Fix the thing")))));

        var issueData = organization.GetIssueData(1, 1, 0, 0);

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateIssueMcpService(testScope).MoveIssueStatus(
            AuthDataFor(organization.Id, memberId), issueData.Key, "In Progress", CancellationToken.None));
    }

    [Fact]
    public async Task MoveIssueStatus_ShouldThrow_WhenStatusBelongsToDifferentEpic()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, space => space
                .AddEpic(ownerId, epic => epic
                    .AddIssue(ownerId, 0, issue => issue.WithContent("Fix the thing")))
                .AddEpic(ownerId, epic => epic
                    .AddStatus(s => s.WithName("OtherEpicStatus")))));

        var issueData = organization.GetIssueData(1, 1, 0, 0);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateIssueMcpService(testScope).MoveIssueStatus(
            AuthDataFor(organization.Id, ownerId), issueData.Key, "OtherEpicStatus", CancellationToken.None));
    }

    [Fact]
    public async Task CreateIssue_ShouldUseSpaceDefaultStatus_WhenStatusNameOmitted()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);

        var space = organization.GetSpace(0);
        var defaultStatus = organization.GetStatus(0, 0, 0);

        var issueKey = await CreateIssueMcpService(testScope).CreateIssue(
            AuthDataFor(organization.Id, ownerId), space.Key, "New issue content", null, CancellationToken.None);

        var createdIssue = await testScope.Database.Issues
            .Where(x => x.IssueNumber!.Space!.Key == space.Key)
            .Select(x => new { x.Content, x.StatusId, x.AssigneeId, x.IssueNumber!.Number })
            .SingleAsyncEF();
        Assert.Equal("New issue content", createdIssue.Content);
        Assert.Equal(defaultStatus.Id, createdIssue.StatusId);
        Assert.Equal(ownerId, createdIssue.AssigneeId);
        Assert.EndsWith(createdIssue.Number.ToString(), issueKey);
    }

    [Fact]
    public async Task CreateIssue_ShouldUseNamedStatus_WhenStatusNameGiven()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);

        var space = organization.GetSpace(0);
        var namedStatus = organization.GetStatus(0, 0, 0); // seeded with name "New"

        await CreateIssueMcpService(testScope).CreateIssue(
            AuthDataFor(organization.Id, ownerId), space.Key, "New issue content", "New", CancellationToken.None);

        var createdIssue = await testScope.Database.Issues
            .SingleAsyncEF(x => x.IssueNumber!.Space!.Key == space.Key);
        Assert.Equal(namedStatus.Id, createdIssue.StatusId);
    }

    [Fact]
    public async Task CreateIssue_ShouldThrow_WhenCallerCannotCreateIssues()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder.SetGlobalAccessLevel(x => x.CanRead = true)));

        var space = organization.GetSpace(0);

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateIssueMcpService(testScope).CreateIssue(
            AuthDataFor(organization.Id, memberId), space.Key, "New issue content", null, CancellationToken.None));
    }

    [Fact]
    public async Task EditIssue_ShouldReplaceContent_WhenCallerCanUpdateIssues()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Original content")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);

        await CreateIssueMcpService(testScope).EditIssue(
            AuthDataFor(organization.Id, ownerId), issueData.Key, "Updated content", CancellationToken.None);

        var updatedIssue = await testScope.Database.Issues.SingleAsyncEF(x => x.Id == issueData.Issue.Id);
        Assert.Equal("Updated content", updatedIssue.Content);
    }

    [Fact]
    public async Task AddComment_ShouldAddComment_WhenCallerCanUpdateIssues()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);

        var commentId = await CreateIssueMcpService(testScope).AddComment(
            AuthDataFor(organization.Id, ownerId), issueData.Key, "A new comment", CancellationToken.None);

        var comment = await testScope.Database.IssueComments.SingleAsyncEF(x => x.Id == commentId);
        Assert.Equal("A new comment", comment.Text);
        Assert.Equal(ownerId, comment.OwnerId);
    }

    [Fact]
    public async Task EditComment_ShouldReplaceText_WhenCallerOwnsComment()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue
                .WithContent("Fix the thing")
                .AddComment(ownerId, "Original comment")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var commentId = issueData.Issue.IssueComments!.Single().Id;

        await CreateIssueMcpService(testScope).EditComment(
            AuthDataFor(organization.Id, ownerId), commentId, "Edited comment", CancellationToken.None);

        var updatedComment = await testScope.Database.IssueComments.SingleAsyncEF(x => x.Id == commentId);
        Assert.Equal("Edited comment", updatedComment.Text);
    }

    [Fact]
    public async Task EditComment_ShouldThrow_WhenCallerDoesNotOwnComment()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder.SetGlobalAccessLevel(x =>
            {
                x.CanRead = true;
                x.CanUpdateIssues = true;
            }))
            .AddIssueToDefaultStatus(ownerId, issue => issue
                .WithContent("Fix the thing")
                .AddComment(ownerId, "Original comment")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var commentId = issueData.Issue.IssueComments!.Single().Id;

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateIssueMcpService(testScope).EditComment(
            AuthDataFor(organization.Id, memberId), commentId, "Edited comment", CancellationToken.None));
    }

    [Fact]
    public async Task EditComment_ShouldThrowNotFound_WhenCommentDoesNotExist()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateIssueMcpService(testScope).EditComment(
            AuthDataFor(organization.Id, ownerId), commentId: 999_999, "Edited comment", CancellationToken.None));
    }
}
