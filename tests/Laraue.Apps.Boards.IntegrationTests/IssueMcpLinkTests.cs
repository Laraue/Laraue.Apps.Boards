using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.McpHost.Services;
using Laraue.Apps.Boards.Services;
using Laraue.Core.Exceptions.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class IssueMcpLinkTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task CreateIssueLink_ShouldShowTheLinkOnBothIssues_AndLeaveTheBlockedOneOutOfListIssues()
    {
        using var testScope = host.CreateTestScope();
        var (userId, _, organization) = await Init(testScope);
        var blocked = organization.GetIssueData(1, 1, 0, 0).Key;
        var blocker = organization.GetIssueData(1, 1, 0, 1).Key;
        var authData = IssueMcpServiceTests.AuthDataFor(organization.Id, userId);
        var mcpService = IssueMcpServiceTests.CreateIssueMcpService(testScope);

        await mcpService.CreateIssueLink(authData, blocked, "blockedBy", blocker, CancellationToken.None);

        var blockedLink = Assert.Single((await mcpService.GetIssue(authData, blocked, CancellationToken.None)).Links);
        Assert.Equal("BlockedBy", blockedLink.Relation);
        Assert.Equal(blocker, blockedLink.Key);
        Assert.Equal("Blocker", blockedLink.Title);

        var blockerLink = Assert.Single((await mcpService.GetIssue(authData, blocker, CancellationToken.None)).Links);
        Assert.Equal("Blocks", blockerLink.Relation);
        Assert.Equal(blocked, blockerLink.Key);

        var allIssues = await mcpService.ListIssues(authData, "LNK", null, null, null, null, true, null, null, CancellationToken.None);
        Assert.Equal(2, allIssues.Issues.Count);

        var readyIssues = await mcpService.ListIssues(authData, "LNK", null, null, null, null, false, null, null, CancellationToken.None);
        Assert.Equal([blocker], readyIssues.Issues.Select(x => x.Key));
    }

    [Fact]
    public async Task CreateIssueLink_ShouldWriteBothIssuesHistory_AsTheApiKey()
    {
        using var testScope = host.CreateTestScope();
        var (userId, _, organization) = await Init(testScope);
        var issue = organization.GetIssueData(1, 1, 0, 0).Key;
        var original = organization.GetIssueData(1, 1, 0, 1).Key;
        var apiKey = await testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(organization.Id, userId, "Claude", CancellationToken.None);
        var authData = IssueMcpServiceTests.AuthDataFor(organization.Id, userId, apiKey.Id);
        var mcpService = IssueMcpServiceTests.CreateIssueMcpService(testScope);

        await mcpService.CreateIssueLink(authData, issue, "Duplicates", original, CancellationToken.None);

        var issueEntry = Assert.Single((await mcpService.GetIssueHistory(authData, issue, null, null, CancellationToken.None)).Entries);
        Assert.Equal("Claude", issueEntry.ApiKeyName);
        Assert.Equal([$"link added: duplicates {original}"], issueEntry.Changes);

        var originalEntry = Assert.Single((await mcpService.GetIssueHistory(authData, original, null, null, CancellationToken.None)).Entries);
        Assert.Equal([$"link added: is duplicated by {issue}"], originalEntry.Changes);
    }

    [Fact]
    public async Task CreateIssueLink_ShouldThrowBadRequest_WhenTheRelationIsUnknown()
    {
        using var testScope = host.CreateTestScope();
        var (userId, _, organization) = await Init(testScope);
        var mcpService = IssueMcpServiceTests.CreateIssueMcpService(testScope);

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => mcpService.CreateIssueLink(
            IssueMcpServiceTests.AuthDataFor(organization.Id, userId),
            organization.GetIssueData(1, 1, 0, 0).Key,
            "DependsOn",
            organization.GetIssueData(1, 1, 0, 1).Key,
            CancellationToken.None));

        Assert.Contains("Blocks, BlockedBy, RelatesTo, Duplicates, DuplicatedBy", exception.Errors["relation"]![0]);
    }

    [Fact]
    public async Task CreateIssueLink_ShouldThrowNotFound_WhenTheOtherIssueIsNotReadable()
    {
        using var testScope = host.CreateTestScope();
        var (_, memberId, organization) = await Init(testScope);
        var mcpService = IssueMcpServiceTests.CreateIssueMcpService(testScope);

        await Assert.ThrowsAsync<NotFoundException>(() => mcpService.CreateIssueLink(
            IssueMcpServiceTests.AuthDataFor(organization.Id, memberId),
            organization.GetIssueData(1, 1, 0, 0).Key,
            "RelatesTo",
            organization.GetIssueData(0, 0, 0, 0).Key,
            CancellationToken.None));
    }

    [Fact]
    public async Task DeleteIssueLink_ShouldRemoveTheLink_WhenCalledFromTheOtherIssue()
    {
        using var testScope = host.CreateTestScope();
        var (userId, _, organization) = await Init(testScope);
        var issue = organization.GetIssueData(1, 1, 0, 0).Key;
        var otherIssue = organization.GetIssueData(1, 1, 0, 1).Key;
        var authData = IssueMcpServiceTests.AuthDataFor(organization.Id, userId);
        var mcpService = IssueMcpServiceTests.CreateIssueMcpService(testScope);
        await mcpService.CreateIssueLink(authData, issue, "RelatesTo", otherIssue, CancellationToken.None);

        await mcpService.DeleteIssueLink(authData, otherIssue, issue, CancellationToken.None);

        Assert.Empty((await mcpService.GetIssue(authData, issue, CancellationToken.None)).Links);
    }

    /// <summary>
    /// The owner and a member who can read and update only the second space ("LNK", index 1), whose added
    /// epic (index 1) has two issues - "Blocked" and "Blocker". The default space has one issue the member
    /// can't read.
    /// </summary>
    private static async Task<(Guid UserId, Guid MemberId, Organization Organization)> Init(WebApiTestHostScope testScope)
    {
        var userId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, o => o
            .AddUser(memberId, u => u.SetSpaceAccessLevel(1, access =>
            {
                access.CanRead = true;
                access.CanUpdateIssues = true;
            }))
            .AddIssueToDefaultStatus(userId, issue => issue.WithTitle("Hidden"))
            .AddSpace(userId, "LNK", space => space
                .AddEpic(userId, epic => epic
                    .AddIssue(userId, 0, issue => issue.WithTitle("Blocked"))
                    .AddIssue(userId, 0, issue => issue.WithTitle("Blocker")))));

        return (userId, memberId, organization);
    }
}
