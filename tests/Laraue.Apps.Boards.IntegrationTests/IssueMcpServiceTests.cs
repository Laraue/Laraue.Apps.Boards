using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess.Models;
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
    // Mirrors IssueMcpService's own MaxResults - the page size returned per ListIssues call.
    private const int IssuesPerPage = 50;

    private static IIssueMcpService CreateIssueMcpService(WebApiTestHostScope testScope)
    {
        return new IssueMcpService(
            testScope.Database,
            testScope.Services.GetRequiredService<IAccessService>(),
            testScope.Services.GetRequiredService<ICoreIssuesService>(),
            testScope.Services.GetRequiredService<ICoreSpacesService>(),
            testScope.Services.GetRequiredService<ICoreIssueAttributesService>(),
            testScope.Services.GetRequiredService<ICoreFilesService>(),
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
            AuthDataFor(organization.Id, ownerId), null, null, null, null, null, CancellationToken.None);
        var memberIssues = await issueMcpService.ListIssues(
            AuthDataFor(organization.Id, memberId), null, null, null, null, null, CancellationToken.None);

        var issue = Assert.Single(ownerIssues.Issues);
        Assert.Equal(issueData.Key, issue.Key);
        Assert.Equal("Fix the thing", issue.Title);
        Assert.Equal(expectedStatus.Name, issue.Status);
        Assert.Equal(ownerDisplayName, issue.Assignee);
        Assert.False(ownerIssues.HasNextPage);
        Assert.Empty(memberIssues.Issues);
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
        var inProgressStatusId = organization.GetStatus(1, 1, 1).Id;

        var issueMcpService = CreateIssueMcpService(testScope);
        var authData = AuthDataFor(organization.Id, ownerId);

        var bySpace = await issueMcpService.ListIssues(
            authData, organization.GetSpace(1).Key, null, null, null, null, CancellationToken.None);
        var byStatus = await issueMcpService.ListIssues(
            authData, null, inProgressStatusId, null, null, null, CancellationToken.None);
        var byAssignee = await issueMcpService.ListIssues(
            authData, null, null, otherAssignee, null, null, CancellationToken.None);

        Assert.Equal(
            new HashSet<string> { targetIssueData.Key, wrongStatusIssueKey },
            bySpace.Issues.Select(x => x.Key).ToHashSet());
        Assert.Equal(targetIssueData.Key, Assert.Single(byStatus.Issues).Key);
        Assert.Equal(targetIssueData.Key, Assert.Single(byAssignee.Issues).Key);
    }

    [Fact]
    public async Task ListIssues_ShouldPaginate_WhenMoreIssuesExistThanOnePage()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org =>
        {
            for (var i = 0; i < IssuesPerPage + 1; i++)
                org.AddIssueToDefaultStatus(ownerId, issue => issue.WithContent($"Issue {i}"));
        });

        var issueMcpService = CreateIssueMcpService(testScope);
        var authData = AuthDataFor(organization.Id, ownerId);

        var firstPage = await issueMcpService.ListIssues(authData, null, null, null, null, null, CancellationToken.None);
        var secondPage = await issueMcpService.ListIssues(authData, null, null, null, 1, null, CancellationToken.None);

        Assert.Equal(IssuesPerPage, firstPage.Issues.Count);
        Assert.True(firstPage.HasNextPage);
        Assert.Equal(0, firstPage.Page);

        Assert.Single(secondPage.Issues);
        Assert.False(secondPage.HasNextPage);
        Assert.Equal(1, secondPage.Page);

        Assert.Empty(firstPage.Issues.Select(x => x.Key).Intersect(secondPage.Issues.Select(x => x.Key)));
    }

    [Fact]
    public async Task ListIssues_ShouldRespectCount_WhenGiven()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org =>
        {
            for (var i = 0; i < 5; i++)
                org.AddIssueToDefaultStatus(ownerId, issue => issue.WithContent($"Issue {i}"));
        });

        var issueMcpService = CreateIssueMcpService(testScope);
        var authData = AuthDataFor(organization.Id, ownerId);

        var firstPage = await issueMcpService.ListIssues(authData, null, null, null, null, 2, CancellationToken.None);
        var secondPage = await issueMcpService.ListIssues(authData, null, null, null, 1, 2, CancellationToken.None);
        var clampedPage = await issueMcpService.ListIssues(authData, null, null, null, null, 1000, CancellationToken.None);

        Assert.Equal(2, firstPage.Issues.Count);
        Assert.True(firstPage.HasNextPage);

        Assert.Equal(2, secondPage.Issues.Count);
        Assert.True(secondPage.HasNextPage);

        Assert.Equal(5, clampedPage.Issues.Count);
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
        Assert.Equal(issueData.Issue.CreatedAt, detail.CreatedAt, new TimeSpan(10));
        Assert.Equal(issueData.Issue.UpdatedAt, detail.UpdatedAt, new TimeSpan(10));

        var comment = Assert.Single(detail.Comments);
        Assert.Equal(expectedComment.Id, comment.Id);
        Assert.Equal(ownerDisplayName, comment.Author);
        Assert.Equal("First comment", comment.Text);
        Assert.Equal(expectedComment.CreatedAt, comment.CreatedAt, new TimeSpan(10));
    }

    [Fact]
    public async Task EditIssueStatus_ShouldUpdateStatus_WhenCallerCanUpdateIssues()
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

        await CreateIssueMcpService(testScope).EditIssueStatus(
            AuthDataFor(organization.Id, ownerId), issueData.Key, targetStatus.Id, CancellationToken.None);

        var updatedIssue = await testScope.Database.Issues.SingleAsyncEF(x => x.Id == issueData.Issue.Id);
        Assert.Equal(targetStatus.Id, updatedIssue.StatusId);
    }

    [Fact]
    public async Task EditIssueStatus_ShouldThrow_WhenCallerLacksUpdatePermission()
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
        var targetStatusId = organization.GetStatus(1, 1, 1).Id;

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateIssueMcpService(testScope).EditIssueStatus(
            AuthDataFor(organization.Id, memberId), issueData.Key, targetStatusId, CancellationToken.None));
    }

    [Fact]
    public async Task EditIssueStatus_ShouldMoveToStatusInDifferentEpic_WhenCallerCanUpdateIssues()
    {
        // Unlike the earlier name-based design (which had to scope status lookup to the issue's
        // own epic to disambiguate a name), a status id has no such ambiguity - and the REST API
        // itself allows moving an issue to any status the caller can access, regardless of epic.
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, space => space
                .AddEpic(ownerId, epic => epic
                    .AddIssue(ownerId, 0, issue => issue.WithContent("Fix the thing")))
                .AddEpic(ownerId, epic => epic
                    .AddStatus(s => s.WithName("OtherEpicStatus")))));

        var issueData = organization.GetIssueData(1, 1, 0, 0);
        var otherEpicStatus = organization.GetStatus(1, 2, 1);

        await CreateIssueMcpService(testScope).EditIssueStatus(
            AuthDataFor(organization.Id, ownerId), issueData.Key, otherEpicStatus.Id, CancellationToken.None);

        var updatedIssue = await testScope.Database.Issues.SingleAsyncEF(x => x.Id == issueData.Issue.Id);
        Assert.Equal(otherEpicStatus.Id, updatedIssue.StatusId);
    }

    [Fact]
    public async Task EditIssueStatus_ShouldThrow_WhenStatusIdDoesNotExist()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateIssueMcpService(testScope).EditIssueStatus(
            AuthDataFor(organization.Id, ownerId), issueData.Key, statusId: 999_999, CancellationToken.None));
    }

    [Fact]
    public async Task CreateIssue_ShouldUseGivenStatus_WhenStatusIdGiven()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, space => space
                .AddEpic(ownerId, epic => epic.AddStatus(s => s.WithName("In Progress")))));

        var space = organization.GetSpace(1);
        var targetStatus = organization.GetStatus(1, 1, 1); // explicit "In Progress", not the epic's implicit default status

        var issueKey = await CreateIssueMcpService(testScope).CreateIssue(
            AuthDataFor(organization.Id, ownerId), "New issue content", targetStatus.Id, null, null, null, CancellationToken.None);

        var createdIssue = await testScope.Database.Issues
            .Where(x => x.IssueNumber!.Space!.Key == space.Key)
            .Select(x => new { x.Content, x.StatusId, x.AssigneeId, x.IssueNumber!.Number })
            .SingleAsyncEF();
        Assert.Equal("New issue content", createdIssue.Content);
        Assert.Equal(targetStatus.Id, createdIssue.StatusId);
        Assert.Equal(ownerId, createdIssue.AssigneeId);
        Assert.EndsWith(createdIssue.Number.ToString(), issueKey);
    }

    [Fact]
    public async Task CreateIssue_ShouldThrow_WhenCallerCannotCreateIssues()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder.SetGlobalAccessLevel(x => x.CanRead = true)));

        var statusId = organization.GetStatus(0, 0, 0).Id;

        // Matches the REST API's own IssuesService.Create - a missing CanCreateIssue is reported
        // as NotFound (via EnsureOrThrowNotFound), not Forbidden.
        await Assert.ThrowsAsync<NotFoundException>(() => CreateIssueMcpService(testScope).CreateIssue(
            AuthDataFor(organization.Id, memberId), "New issue content", statusId, null, null, null, CancellationToken.None));
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
            AuthDataFor(organization.Id, ownerId), issueData.Key, "Updated content", null, null, null, null, CancellationToken.None);

        var updatedIssue = await testScope.Database.Issues.SingleAsyncEF(x => x.Id == issueData.Issue.Id);
        Assert.Equal("Updated content", updatedIssue.Content);
    }

    [Fact]
    public async Task CreateComment_ShouldCreateComment_WhenCallerCanUpdateIssues()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);

        var commentId = await CreateIssueMcpService(testScope).CreateComment(
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

    [Fact]
    public async Task DeleteComment_ShouldSoftDeleteComment_WhenCallerOwnsComment()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue
                .WithContent("Fix the thing")
                .AddComment(ownerId, "Original comment")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var commentId = issueData.Issue.IssueComments!.Single().Id;

        await CreateIssueMcpService(testScope).DeleteComment(
            AuthDataFor(organization.Id, ownerId), commentId, CancellationToken.None);

        var deletedComment = await testScope.Database.IssueComments.SingleAsyncEF(x => x.Id == commentId);
        Assert.NotNull(deletedComment.DeletedAt);
    }

    [Fact]
    public async Task DeleteComment_ShouldThrow_WhenCallerDoesNotOwnComment()
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

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateIssueMcpService(testScope).DeleteComment(
            AuthDataFor(organization.Id, memberId), commentId, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteComment_ShouldThrowNotFound_WhenCommentDoesNotExist()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateIssueMcpService(testScope).DeleteComment(
            AuthDataFor(organization.Id, ownerId), commentId: 999_999, CancellationToken.None));
    }

    [Fact]
    public async Task ListSpaces_ShouldReturnAvailableSpaces_WhenCalled()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId)
            .AddSpace(ownerId, space => space.WithName("Team Board")));

        var defaultSpace = organization.GetSpace(0);
        var extraSpace = organization.GetSpace(1);

        var ownerSpaces = await CreateIssueMcpService(testScope)
            .ListSpaces(AuthDataFor(organization.Id, ownerId), CancellationToken.None);
        var memberSpaces = await CreateIssueMcpService(testScope)
            .ListSpaces(AuthDataFor(organization.Id, memberId), CancellationToken.None);

        Assert.Equal(
            new HashSet<string> { defaultSpace.Key, extraSpace.Key },
            ownerSpaces.Select(x => x.Key).ToHashSet());
        Assert.Contains(ownerSpaces, x => x.Key == extraSpace.Key && x.Name == "Team Board");

        // The member has no CanRead grant on either space, so neither is visible to them.
        Assert.Empty(memberSpaces);
    }

    [Fact]
    public async Task ListSpaces_ShouldExposeCanCreateIssue_BasedOnCallerPermissions()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder.SetGlobalAccessLevel(x =>
            {
                x.CanRead = true;
                x.CanCreateIssues = false;
            })));

        var space = organization.GetSpace(0);

        var ownerSpaces = await CreateIssueMcpService(testScope)
            .ListSpaces(AuthDataFor(organization.Id, ownerId), CancellationToken.None);
        var memberSpaces = await CreateIssueMcpService(testScope)
            .ListSpaces(AuthDataFor(organization.Id, memberId), CancellationToken.None);

        Assert.True(Assert.Single(ownerSpaces, x => x.Key == space.Key).CanCreateIssue);
        Assert.False(Assert.Single(memberSpaces, x => x.Key == space.Key).CanCreateIssue);
    }

    [Fact]
    public async Task ListStatuses_ShouldGroupByEpic_WhenCalled()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, space => space
                .AddEpic(ownerId, epic => epic.WithName("Epic A").AddStatus(s => s.WithName("In Progress")))
                .AddEpic(ownerId, epic => epic.WithName("Epic B").AddStatus(s => s.WithName("Review")))));

        var space = organization.GetSpace(1);

        var result = await CreateIssueMcpService(testScope)
            .ListStatuses(AuthDataFor(organization.Id, ownerId), space.Key, CancellationToken.None);

        // Implicit "Backlog" epic + the two explicit ones.
        Assert.Equal(3, result.Count);
        var epicA = Assert.Single(result, x => x.EpicName == "Epic A");
        var epicAStatus = Assert.Single(epicA.Statuses, x => x.Name == "In Progress");
        Assert.Equal(organization.GetStatus(1, 1, 1).Id, epicAStatus.Id);
        var epicB = Assert.Single(result, x => x.EpicName == "Epic B");
        var epicBStatus = Assert.Single(epicB.Statuses, x => x.Name == "Review");
        Assert.Equal(organization.GetStatus(1, 2, 1).Id, epicBStatus.Id);
    }

    [Fact]
    public async Task ListAttributes_ShouldReturnListValues_ForListTypedAttributes()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddTextAttribute("Summary")
            .AddListAttribute("Priority", ["Low", "High"]));

        var result = await CreateIssueMcpService(testScope)
            .ListAttributes(AuthDataFor(organization.Id, ownerId), CancellationToken.None);

        var summaryAttribute = organization.GetAttribute(0);
        var priorityAttribute = organization.GetAttribute(1);

        var summary = Assert.Single(result, x => x.Name == "Summary");
        Assert.Equal(summaryAttribute.Id, summary.Id);
        Assert.Equal(nameof(AttributeType.Text), summary.Type);
        Assert.Null(summary.ListValues);

        var priority = Assert.Single(result, x => x.Name == "Priority");
        Assert.Equal(priorityAttribute.Id, priority.Id);
        Assert.Equal(nameof(AttributeType.List), priority.Type);
        Assert.Equal(
            priorityAttribute.AttributeListValues!.Select(v => (v.Id, v.Value)).ToHashSet(),
            priority.ListValues!.Select(v => (v.Id, v.Value)).ToHashSet());
    }

    [Fact]
    public async Task ListMembers_ShouldReturnOnlyVisibleMembers_WhenCalled()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var outsiderId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder.SetGlobalAccessLevel(x => x.CanRead = true)));

        var ownerDisplayName = await testScope.Database.Users
            .Where(x => x.Id == ownerId)
            .Select(x => x.DisplayName)
            .SingleAsyncEF();
        var memberDisplayName = await testScope.Database.Users
            .Where(x => x.Id == memberId)
            .Select(x => x.DisplayName)
            .SingleAsyncEF();

        var result = await CreateIssueMcpService(testScope)
            .ListMembers(AuthDataFor(organization.Id, ownerId), null, CancellationToken.None);

        Assert.Equal(
            new Dictionary<Guid, string> { [ownerId] = ownerDisplayName, [memberId] = memberDisplayName },
            result.ToDictionary(x => x.Id, x => x.DisplayName));
        Assert.DoesNotContain(result, x => x.Id == outsiderId);
    }

    [Fact]
    public async Task ListMembers_ShouldOnlyReturnMembersVisibleInGivenSpace_WhenSpaceKeyGiven()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var targetSpaceMemberId = await testScope.CreateUser();
        var otherSpaceMemberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, space => space.WithName("Team Board"))
            .AddUser(targetSpaceMemberId, builder => builder.SetSpaceAccessLevel(1, x => x.CanRead = true))
            .AddUser(otherSpaceMemberId, builder => builder.SetSpaceAccessLevel(0, x => x.CanRead = true)));

        var targetSpace = organization.GetSpace(1);

        var result = await CreateIssueMcpService(testScope)
            .ListMembers(AuthDataFor(organization.Id, ownerId), targetSpace.Key, CancellationToken.None);

        Assert.Contains(result, x => x.Id == targetSpaceMemberId);
        Assert.DoesNotContain(result, x => x.Id == otherSpaceMemberId);
    }

    [Fact]
    public async Task ListMembers_ShouldThrow_WhenCallerCannotReadGivenSpace()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder.SetGlobalAccessLevel(x => x.CanRead = false)));

        var space = organization.GetSpace(0);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateIssueMcpService(testScope).ListMembers(
            AuthDataFor(organization.Id, memberId), space.Key, CancellationToken.None));
    }

    [Fact]
    public async Task CreateIssue_ShouldSetAttributes_WhenGiven()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddTextAttribute("Summary")
            .AddListAttribute("Priority", ["Low", "High"]));

        var space = organization.GetSpace(0);
        var statusId = organization.GetStatus(0, 0, 0).Id;
        var summaryAttribute = organization.GetAttribute(0);
        var priorityAttribute = organization.GetAttribute(1);
        var highValue = priorityAttribute.AttributeListValues!.Single(x => x.Value == "High");

        var issueKey = await CreateIssueMcpService(testScope).CreateIssue(
            AuthDataFor(organization.Id, ownerId),
            "New issue content",
            statusId,
            null,
            new Dictionary<long, string>
            {
                [summaryAttribute.Id] = "A short summary",
                [priorityAttribute.Id] = highValue.Id.ToString(),
            },
            null,
            CancellationToken.None);

        var issueId = await testScope.Database.Issues
            .Where(x => x.IssueNumber!.Space!.Key == space.Key)
            .Select(x => x.Id)
            .SingleAsyncEF();

        var textValue = await testScope.Database.IssueAttributeTextValues
            .SingleAsyncEF(x => x.IssueId == issueId && x.AttributeId == summaryAttribute.Id);
        Assert.Equal("A short summary", textValue.Value);

        var listValue = await testScope.Database.IssueAttributeListValues
            .SingleAsyncEF(x => x.IssueId == issueId && x.AttributeId == priorityAttribute.Id);
        Assert.Equal(highValue.Id, listValue.AttributeListValueId);
    }

    [Fact]
    public async Task CreateIssue_ShouldThrow_WhenAttributeListValueIsInvalid()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddListAttribute("Priority", ["Low", "High"]));

        var space = organization.GetSpace(0);
        var statusId = organization.GetStatus(0, 0, 0).Id;
        var priorityAttribute = organization.GetAttribute(0);

        await Assert.ThrowsAsync<BadRequestException>(() => CreateIssueMcpService(testScope).CreateIssue(
            AuthDataFor(organization.Id, ownerId),
            "New issue content",
            statusId,
            null,
            new Dictionary<long, string> { [priorityAttribute.Id] = "999999" }, // no such list value id
            null,
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateIssue_ShouldReportEveryBadAttribute_WhenMultipleAreInvalid()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddListAttribute("Priority", ["Low", "High"])
            .AddIntegerAttribute("Estimate"));

        var space = organization.GetSpace(0);
        var statusId = organization.GetStatus(0, 0, 0).Id;
        var priorityAttribute = organization.GetAttribute(0);
        var estimateAttribute = organization.GetAttribute(1);
        const long unknownAttributeId = 999999;

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => CreateIssueMcpService(testScope).CreateIssue(
            AuthDataFor(organization.Id, ownerId),
            "New issue content",
            statusId,
            null,
            new Dictionary<long, string>
            {
                [priorityAttribute.Id] = "999999", // no such list value id
                [estimateAttribute.Id] = "not a number",
                [unknownAttributeId] = "whatever",
            },
            null,
            CancellationToken.None));

        Assert.Equal(3, exception.Errors["attributes"].Length);
    }

    [Fact]
    public async Task EditIssue_ShouldSetAttributes_WhenGiven()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIntegerAttribute("Estimate")
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var estimateAttribute = organization.GetAttribute(0);

        await CreateIssueMcpService(testScope).EditIssue(
            AuthDataFor(organization.Id, ownerId),
            issueData.Key,
            "Fix the thing",
            null,
            new Dictionary<long, string> { [estimateAttribute.Id] = "5" },
            null,
            null,
            CancellationToken.None);

        var integerValue = await testScope.Database.IssueAttributeIntegerValues
            .SingleAsyncEF(x => x.IssueId == issueData.Issue.Id && x.AttributeId == estimateAttribute.Id);
        Assert.Equal(5, integerValue.Value);
    }

    [Fact]
    public async Task EditIssue_ShouldClearAllAttributes_WhenEmptyDictionaryGiven()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIntegerAttribute("Estimate")
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var estimateAttribute = organization.GetAttribute(0);
        var mcpService = CreateIssueMcpService(testScope);

        await mcpService.EditIssue(
            AuthDataFor(organization.Id, ownerId),
            issueData.Key,
            "Fix the thing",
            null,
            new Dictionary<long, string> { [estimateAttribute.Id] = "5" },
            null,
            null,
            CancellationToken.None);

        // An empty (but non-null) dictionary means "clear every attribute", distinct from
        // omitting the parameter entirely ("leave attributes untouched").
        await mcpService.EditIssue(
            AuthDataFor(organization.Id, ownerId),
            issueData.Key,
            "Fix the thing",
            null,
            new Dictionary<long, string>(),
            null,
            null,
            CancellationToken.None);

        var hasIntegerValue = await testScope.Database.IssueAttributeIntegerValues
            .AnyAsyncEF(x => x.IssueId == issueData.Issue.Id && x.AttributeId == estimateAttribute.Id);
        Assert.False(hasIntegerValue);
    }

    private static string SampleImageBase64() => Convert.ToBase64String([1, 2, 3, 4, 5]);

    [Fact]
    public async Task CreateIssue_ShouldAttachFiles_WhenGiven()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => { });

        var statusId = organization.GetStatus(0, 0, 0).Id;

        await CreateIssueMcpService(testScope).CreateIssue(
            AuthDataFor(organization.Id, ownerId),
            "New issue content",
            statusId,
            null,
            null,
            [new FileAttachment("photo.png", "image/png", SampleImageBase64())],
            CancellationToken.None);

        var issueId = await testScope.Database.Issues
            .Where(x => x.IssueNumber!.Space!.Key == organization.GetSpace(0).Key)
            .Select(x => x.Id)
            .SingleAsyncEF();

        var attachment = await testScope.Database.IssueAttachments
            .Where(x => x.IssueId == issueId)
            .Select(x => new { x.Attachment!.Type })
            .SingleAsyncEF();
        Assert.Equal(AttachmentType.Image, attachment.Type);
    }

    [Fact]
    public async Task CreateIssue_ShouldThrow_WhenFileHasUnsupportedMimeType()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => { });
        var statusId = organization.GetStatus(0, 0, 0).Id;

        await Assert.ThrowsAsync<BadRequestException>(() => CreateIssueMcpService(testScope).CreateIssue(
            AuthDataFor(organization.Id, ownerId),
            "New issue content",
            statusId,
            null,
            null,
            [new FileAttachment("doc.pdf", "application/pdf", SampleImageBase64())],
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateIssue_ShouldThrow_WhenFileBase64IsInvalid()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => { });
        var statusId = organization.GetStatus(0, 0, 0).Id;

        await Assert.ThrowsAsync<BadRequestException>(() => CreateIssueMcpService(testScope).CreateIssue(
            AuthDataFor(organization.Id, ownerId),
            "New issue content",
            statusId,
            null,
            null,
            [new FileAttachment("photo.png", "image/png", "not-valid-base64!!")],
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateIssue_ShouldThrow_WhenFileIsTooLarge()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => { });
        var statusId = organization.GetStatus(0, 0, 0).Id;

        var tooLarge = Convert.ToBase64String(new byte[SystemMimeTypes.MaxFileSizeBytes + 1]);

        await Assert.ThrowsAsync<BadRequestException>(() => CreateIssueMcpService(testScope).CreateIssue(
            AuthDataFor(organization.Id, ownerId),
            "New issue content",
            statusId,
            null,
            null,
            [new FileAttachment("photo.png", "image/png", tooLarge)],
            CancellationToken.None));
    }

    [Fact]
    public async Task EditIssue_ShouldAttachFiles_WhenGiven()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);

        await CreateIssueMcpService(testScope).EditIssue(
            AuthDataFor(organization.Id, ownerId),
            issueData.Key,
            "Fix the thing",
            null,
            null,
            [new FileAttachment("photo.png", "image/png", SampleImageBase64())],
            null,
            CancellationToken.None);

        var attachment = await testScope.Database.IssueAttachments
            .Where(x => x.IssueId == issueData.Issue.Id)
            .Select(x => new { x.Attachment!.Type })
            .SingleAsyncEF();
        Assert.Equal(AttachmentType.Image, attachment.Type);
    }

    [Fact]
    public async Task EditIssue_ShouldRemoveAttachment_WhenRemoveAttachmentIdsGiven()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var mcpService = CreateIssueMcpService(testScope);

        await mcpService.EditIssue(
            AuthDataFor(organization.Id, ownerId),
            issueData.Key,
            "Fix the thing",
            null,
            null,
            [new FileAttachment("photo.png", "image/png", SampleImageBase64())],
            null,
            CancellationToken.None);

        var detailWithAttachment = await mcpService.GetIssue(
            AuthDataFor(organization.Id, ownerId), issueData.Key, CancellationToken.None);
        var attachmentId = Assert.Single(detailWithAttachment.Attachments).Id;

        await mcpService.EditIssue(
            AuthDataFor(organization.Id, ownerId),
            issueData.Key,
            "Fix the thing",
            null,
            null,
            null,
            [attachmentId],
            CancellationToken.None);

        var detailAfterRemoval = await mcpService.GetIssue(
            AuthDataFor(organization.Id, ownerId), issueData.Key, CancellationToken.None);
        Assert.Empty(detailAfterRemoval.Attachments);
    }

    [Fact]
    public async Task GetAttachmentContent_ShouldReturnFileContent_WhenAccessible()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var mcpService = CreateIssueMcpService(testScope);

        await mcpService.EditIssue(
            AuthDataFor(organization.Id, ownerId),
            issueData.Key,
            "Fix the thing",
            null,
            null,
            [new FileAttachment("photo.png", "image/png", SampleImageBase64())],
            null,
            CancellationToken.None);

        var detail = await mcpService.GetIssue(
            AuthDataFor(organization.Id, ownerId), issueData.Key, CancellationToken.None);
        var attachmentId = Assert.Single(detail.Attachments).Id;

        var content = await mcpService.GetAttachmentContent(
            AuthDataFor(organization.Id, ownerId), attachmentId, CancellationToken.None);

        await using var stream = content.Content;
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, CancellationToken.None);

        Assert.Equal("image/png", content.MimeType);
        Assert.NotEmpty(memoryStream.ToArray());
    }

    [Fact]
    public async Task GetAttachmentContent_ShouldThrow_WhenAttachmentDoesNotExist()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateIssueMcpService(testScope).GetAttachmentContent(
            AuthDataFor(organization.Id, ownerId), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetAttachmentContent_ShouldThrow_WhenCallerCannotReadIssue()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder.SetGlobalAccessLevel(x => x.CanRead = false))
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var mcpService = CreateIssueMcpService(testScope);

        await mcpService.EditIssue(
            AuthDataFor(organization.Id, ownerId),
            issueData.Key,
            "Fix the thing",
            null,
            null,
            [new FileAttachment("photo.png", "image/png", SampleImageBase64())],
            null,
            CancellationToken.None);

        var detail = await mcpService.GetIssue(
            AuthDataFor(organization.Id, ownerId), issueData.Key, CancellationToken.None);
        var attachmentId = Assert.Single(detail.Attachments).Id;

        await Assert.ThrowsAsync<NotFoundException>(() => mcpService.GetAttachmentContent(
            AuthDataFor(organization.Id, memberId), attachmentId, CancellationToken.None));
    }

    [Fact]
    public async Task GetAttachmentContent_ShouldThrow_WhenFileExceedsDownloadSizeLimit()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var mcpService = CreateIssueMcpService(testScope);

        await mcpService.EditIssue(
            AuthDataFor(organization.Id, ownerId),
            issueData.Key,
            "Fix the thing",
            null,
            null,
            [new FileAttachment("photo.png", "image/png", SampleImageBase64())],
            null,
            CancellationToken.None);

        var detail = await mcpService.GetIssue(
            AuthDataFor(organization.Id, ownerId), issueData.Key, CancellationToken.None);
        var attachmentId = Assert.Single(detail.Attachments).Id;

        // Uploads are already capped at SystemMimeTypes.MaxFileSizeBytes, so simulate a
        // legacy/otherwise-oversized file by overwriting its recorded size directly.
        var fileId = await testScope.Database.Attachments
            .Where(x => x.Id == attachmentId)
            .Select(x => x.FileId)
            .SingleAsyncEF();
        await testScope.Database.Files
            .Where(x => x.Id == fileId)
            .ExecuteUpdateAsync(x => x.SetProperty(f => f.Size, SystemMimeTypes.MaxFileSizeBytes + 1));

        await Assert.ThrowsAsync<BadRequestException>(() => mcpService.GetAttachmentContent(
            AuthDataFor(organization.Id, ownerId), attachmentId, CancellationToken.None));
    }

    [Fact]
    public async Task CreateIssue_ShouldAssignToGivenUser_WhenAssigneeIdGiven()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder.SetGlobalAccessLevel(x => x.CanRead = true)));

        var statusId = organization.GetStatus(0, 0, 0).Id;

        await CreateIssueMcpService(testScope).CreateIssue(
            AuthDataFor(organization.Id, ownerId), "New issue content", statusId, memberId, null, null, CancellationToken.None);

        var assigneeId = await testScope.Database.Issues
            .Where(x => x.IssueNumber!.Space!.Key == organization.GetSpace(0).Key)
            .Select(x => x.AssigneeId)
            .SingleAsyncEF();
        Assert.Equal(memberId, assigneeId);
    }

    [Fact]
    public async Task CreateIssue_ShouldThrow_WhenAssigneeDoesNotBelongToOrganization()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);
        var statusId = organization.GetStatus(0, 0, 0).Id;
        var outsiderId = await testScope.CreateUser();

        await Assert.ThrowsAsync<NotFoundException>(() => CreateIssueMcpService(testScope).CreateIssue(
            AuthDataFor(organization.Id, ownerId), "New issue content", statusId, outsiderId, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task EditIssue_ShouldReassignIssue_WhenAssigneeIdGiven()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder.SetGlobalAccessLevel(x => x.CanRead = true))
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);

        await CreateIssueMcpService(testScope).EditIssue(
            AuthDataFor(organization.Id, ownerId), issueData.Key, "Fix the thing", memberId, null, null, null, CancellationToken.None);

        var updatedIssue = await testScope.Database.Issues.SingleAsyncEF(x => x.Id == issueData.Issue.Id);
        Assert.Equal(memberId, updatedIssue.AssigneeId);
    }

    [Fact]
    public async Task EditIssue_ShouldLeaveAssigneeUnchanged_WhenAssigneeIdOmitted()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);

        await CreateIssueMcpService(testScope).EditIssue(
            AuthDataFor(organization.Id, ownerId), issueData.Key, "Updated content", null, null, null, null, CancellationToken.None);

        var updatedIssue = await testScope.Database.Issues.SingleAsyncEF(x => x.Id == issueData.Issue.Id);
        Assert.Equal(ownerId, updatedIssue.AssigneeId);
    }

    [Fact]
    public async Task EditIssue_ShouldThrow_WhenAssigneeDoesNotBelongToOrganization()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var outsiderId = await testScope.CreateUser();

        await Assert.ThrowsAsync<NotFoundException>(() => CreateIssueMcpService(testScope).EditIssue(
            AuthDataFor(organization.Id, ownerId), issueData.Key, "Fix the thing", outsiderId, null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteIssue_ShouldSoftDeleteIssue_WhenCallerCanDeleteIssues()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueData = organization.GetIssueData(0, 0, 0, 0);

        await CreateIssueMcpService(testScope).DeleteIssue(
            AuthDataFor(organization.Id, ownerId), issueData.Key, CancellationToken.None);

        var deletedIssue = await testScope.Database.Issues.SingleAsyncEF(x => x.Id == issueData.Issue.Id);
        Assert.NotNull(deletedIssue.DeletedAt);
    }

    [Fact]
    public async Task DeleteIssue_ShouldThrow_WhenCallerLacksDeletePermission()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder.SetGlobalAccessLevel(x => x.CanRead = true))
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueData = organization.GetIssueData(0, 0, 0, 0);

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateIssueMcpService(testScope).DeleteIssue(
            AuthDataFor(organization.Id, memberId), issueData.Key, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteIssue_ShouldThrow_WhenIssueDoesNotExist()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);
        var missingKey = new IssueKey(organization.GetSpace(0).Key, 999_999).ToString();

        await Assert.ThrowsAsync<NotFoundException>(() => CreateIssueMcpService(testScope).DeleteIssue(
            AuthDataFor(organization.Id, ownerId), missingKey, CancellationToken.None));
    }

    [Fact]
    public async Task ListIssues_ShouldExposeCanEditAndCanDelete_BasedOnCallerPermissions()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder.SetGlobalAccessLevel(x =>
            {
                x.CanRead = true;
                x.CanUpdateIssues = true;
                x.CanDeleteIssues = false;
            }))
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));

        var issueMcpService = CreateIssueMcpService(testScope);

        var ownerIssues = await issueMcpService.ListIssues(
            AuthDataFor(organization.Id, ownerId), null, null, null, null, null, CancellationToken.None);
        var memberIssues = await issueMcpService.ListIssues(
            AuthDataFor(organization.Id, memberId), null, null, null, null, null, CancellationToken.None);

        var ownerIssue = Assert.Single(ownerIssues.Issues);
        Assert.True(ownerIssue.CanEdit);
        Assert.True(ownerIssue.CanDelete);

        var memberIssue = Assert.Single(memberIssues.Issues);
        Assert.True(memberIssue.CanEdit);
        Assert.False(memberIssue.CanDelete);
    }

    [Fact]
    public async Task GetIssue_ShouldExposeCanEditAndCanDelete_BasedOnCallerPermissions()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder.SetGlobalAccessLevel(x =>
            {
                x.CanRead = true;
                x.CanUpdateIssues = false;
                x.CanDeleteIssues = false;
            }))
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var issueMcpService = CreateIssueMcpService(testScope);

        var ownerDetail = await issueMcpService.GetIssue(
            AuthDataFor(organization.Id, ownerId), issueData.Key, CancellationToken.None);
        var memberDetail = await issueMcpService.GetIssue(
            AuthDataFor(organization.Id, memberId), issueData.Key, CancellationToken.None);

        Assert.True(ownerDetail.CanEdit);
        Assert.True(ownerDetail.CanDelete);
        Assert.False(memberDetail.CanEdit);
        Assert.False(memberDetail.CanDelete);
    }
}
