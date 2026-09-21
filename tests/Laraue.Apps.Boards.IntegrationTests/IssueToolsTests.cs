using System.Security.Claims;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.McpHost.Tools;
using Laraue.Apps.Boards.Services;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Exercises <see cref="IssueTools"/> directly, bypassing the actual MCP HTTP/SSE transport -
/// there's no permission/mutation logic here that isn't already covered by
/// <see cref="IAccessService"/>/<c>ICoreIssuesService</c> themselves, so this only needs to confirm
/// the tools wire the caller's identity and those checks together correctly.
/// </summary>
[Collection("IntegrationTest")]
public class IssueToolsTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private static IssueTools CreateIssueTools(WebApiTestHostScope testScope, long organizationId, Guid userId)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim("orgId", organizationId.ToString()),
                new Claim("id", userId.ToString()),
            ],
            "Test");

        var httpContextAccessor = new FakeHttpContextAccessor(
            new DefaultHttpContext { User = new ClaimsPrincipal(identity) });

        return new IssueTools(
            testScope.Database,
            testScope.Services.GetRequiredService<IAccessService>(),
            testScope.Services.GetRequiredService<ICoreIssuesService>(),
            httpContextAccessor);
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

        var ownerIssues = await CreateIssueTools(testScope, organization.Id, ownerId)
            .ListIssues(null, null, null, CancellationToken.None);
        var memberIssues = await CreateIssueTools(testScope, organization.Id, memberId)
            .ListIssues(null, null, null, CancellationToken.None);

        Assert.Single(ownerIssues);
        Assert.Empty(memberIssues);
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

        var detail = await CreateIssueTools(testScope, organization.Id, ownerId)
            .GetIssue(issueData.Key, CancellationToken.None);

        Assert.Equal("Fix the thing", detail.Content);
        var comment = Assert.Single(detail.Comments);
        Assert.Equal("First comment", comment.Text);
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

        await CreateIssueTools(testScope, organization.Id, ownerId)
            .MoveIssueStatus(issueData.Key, "In Progress", CancellationToken.None);

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

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateIssueTools(testScope, organization.Id, memberId)
            .MoveIssueStatus(issueData.Key, "In Progress", CancellationToken.None));
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

        await Assert.ThrowsAsync<NotFoundException>(() => CreateIssueTools(testScope, organization.Id, ownerId)
            .MoveIssueStatus(issueData.Key, "OtherEpicStatus", CancellationToken.None));
    }

    private sealed class FakeHttpContextAccessor(HttpContext httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
    }
}
