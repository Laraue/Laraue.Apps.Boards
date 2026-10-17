using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class IssueLinksControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<IssuesController> _issuesController = host.Controller<IssuesController>();

    [Fact]
    public async Task AddLink_ShouldShowTheLinkOnBothIssues_FromEachIssueSide()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope);
        var blocked = setup.Organization.GetIssueData(1, 1, 0, 0);
        var blocker = setup.Organization.GetIssueData(1, 1, 0, 1);

        await AddLink(setup.Organization, setup.UserId, blocked.Key, IssueLinkRelation.BlockedBy, blocker.Key);

        var blockedLink = Assert.Single((await GetIssue(setup.Organization, setup.UserId, blocked.Key)).Links);
        Assert.Equal(IssueLinkRelation.BlockedBy, blockedLink.Relation);
        Assert.Equal(blocker.Key, blockedLink.Key);
        Assert.Equal("Blocker", blockedLink.Title);
        Assert.Equal(StatusCategory.Created, blockedLink.StatusCategory);

        var blockerLink = Assert.Single((await GetIssue(setup.Organization, setup.UserId, blocker.Key)).Links);
        Assert.Equal(IssueLinkRelation.Blocks, blockerLink.Relation);
        Assert.Equal(blocked.Key, blockerLink.Key);
    }

    [Fact]
    public async Task RemoveLink_ShouldRemoveTheLinkFromBothIssues_WhenRemovedFromTheOtherSide()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope);
        var issue = setup.Organization.GetIssueData(1, 1, 0, 0);
        var otherIssue = setup.Organization.GetIssueData(1, 1, 0, 1);
        await AddLink(setup.Organization, setup.UserId, issue.Key, IssueLinkRelation.Duplicates, otherIssue.Key);

        await _issuesController
            .WithOrganizationAuthorization(setup.Organization.Id, setup.UserId)
            .Execute(x => x.RemoveLink(otherIssue.Key, issue.Key));

        Assert.Empty((await GetIssue(setup.Organization, setup.UserId, issue.Key)).Links);
        Assert.Empty((await GetIssue(setup.Organization, setup.UserId, otherIssue.Key)).Links);
    }

    [Fact]
    public async Task AddLink_ShouldReturnNotFound_WhenTheOtherIssueIsNotReadable()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope, member => member.CanUpdateIssues = true);
        var readable = setup.Organization.GetIssueData(1, 1, 0, 0);
        var notReadable = setup.Organization.GetIssueData(0, 0, 0, 0);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => AddLink(setup.Organization, setup.MemberId, readable.Key, IssueLinkRelation.RelatesTo, notReadable.Key));

        var notFound = ex.HasInnerException<NotFoundException>();
        Assert.Equal($"Issue: {notReadable.Key} is not found or not accessible", notFound.Message);
    }

    [Fact]
    public async Task AddLink_ShouldReturnNotFound_WhenTheIssueIsNotReadable()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope, member => member.CanUpdateIssues = true);
        var notReadable = setup.Organization.GetIssueData(0, 0, 0, 0);
        var readable = setup.Organization.GetIssueData(1, 1, 0, 0);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => AddLink(setup.Organization, setup.MemberId, notReadable.Key, IssueLinkRelation.RelatesTo, readable.Key));

        ex.HasInnerException<NotFoundException>();
    }

    [Fact]
    public async Task AddLink_ShouldReturnForbidden_WhenTheIssueIsReadableButNotUpdatable()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope, _ => { });
        var issue = setup.Organization.GetIssueData(1, 1, 0, 0);
        var otherIssue = setup.Organization.GetIssueData(1, 1, 0, 1);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => AddLink(setup.Organization, setup.MemberId, issue.Key, IssueLinkRelation.RelatesTo, otherIssue.Key));

        ex.HasInnerException<ForbiddenException>();
    }

    [Fact]
    public async Task GetIssue_ShouldHideLinksToIssuesTheCallerCannotRead()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope, _ => { });
        var readable = setup.Organization.GetIssueData(1, 1, 0, 0);
        var notReadable = setup.Organization.GetIssueData(0, 0, 0, 0);
        await AddLink(setup.Organization, setup.UserId, readable.Key, IssueLinkRelation.BlockedBy, notReadable.Key);

        Assert.Empty((await GetIssue(setup.Organization, setup.MemberId, readable.Key)).Links);
        Assert.Single((await GetIssue(setup.Organization, setup.UserId, readable.Key)).Links);
    }

    [Fact]
    public async Task GetBoard_ShouldMarkTheIssueBlocked_UntilItsBlockerIsCompleted()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope);
        var blocked = setup.Organization.GetIssueData(1, 1, 0, 0);
        var blocker = setup.Organization.GetIssueData(1, 1, 0, 1);
        await AddLink(setup.Organization, setup.UserId, blocker.Key, IssueLinkRelation.Blocks, blocked.Key);

        var board = await GetBoardIssues(setup.Organization, setup.UserId);
        Assert.True(board[blocked.Key].IsBlocked);
        Assert.False(board[blocker.Key].IsBlocked);

        var completedStatusId = setup.Organization.GetStatus(1, 1, 1).Id;
        await testScope.Database.Issues
            .Where(x => x.Id == blocker.Issue.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.StatusId, completedStatusId));

        board = await GetBoardIssues(setup.Organization, setup.UserId);
        Assert.False(board[blocked.Key].IsBlocked);
    }

    private sealed record LinkTestSetup(Guid UserId, Guid MemberId, Organization Organization);

    /// <summary>
    /// The owner and a member who can read only the second space (index 1), whose added epic (index 1) has
    /// two issues - "Blocked" and "Blocker" - and a Completed status (index 1). The default space has one
    /// issue the member can't read.
    /// </summary>
    private static async Task<LinkTestSetup> Init(
        WebApiTestHostScope testScope,
        Action<DirectSpaceAccessLevel>? memberAccess = null)
    {
        var userId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, o =>
        {
            o
                .AddIssueToDefaultStatus(userId, issue => issue.WithContent("Hidden"))
                .AddSpace(userId, "LNK", space => space
                    .AddEpic(userId, epic => epic
                        .AddStatus(s => s.WithCategory(StatusCategory.Completed))
                        .AddIssue(userId, 0, issue => issue.WithTitle("Blocked"))
                        .AddIssue(userId, 0, issue => issue.WithTitle("Blocker"))));

            if (memberAccess is not null)
                o.AddUser(memberId, u => u.SetSpaceAccessLevel(1, access =>
                {
                    access.CanRead = true;
                    memberAccess(access);
                }));
        });

        return new LinkTestSetup(userId, memberId, organization);
    }

    private Task AddLink(
        Organization organization,
        Guid userId,
        string issueKey,
        IssueLinkRelation relation,
        string otherIssueKey)
    {
        return _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.AddLink(
                issueKey,
                new AddIssueLinkRequest
                {
                    Relation = relation,
                    OtherIssueKey = otherIssueKey,
                }));
    }

    private async Task<IssueDetailDto> GetIssue(Organization organization, Guid userId, string issueKey)
    {
        var issue = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetIssue(issueKey));

        return issue!;
    }

    private async Task<Dictionary<string, IssueListDto>> GetBoardIssues(Organization organization, Guid userId)
    {
        var columns = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetBoard(
                new GetBoardRequest
                {
                    EpicId = organization.GetEpic(1, 1).Id,
                    Take = 10,
                }));

        return columns!
            .SelectMany(x => x.Items.Data)
            .ToDictionary(x => x.Key);
    }
}
