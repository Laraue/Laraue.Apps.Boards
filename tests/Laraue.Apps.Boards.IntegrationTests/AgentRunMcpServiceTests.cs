using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.McpHost.Services;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Links;
using Laraue.Apps.Boards.Services.Members;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Agent runs through <see cref="AgentRunMcpService"/> (BRD-305), against the real test database - the claim race
/// and the "one open run per issue" rule are both enforced by Postgres, so they need it.
/// </summary>
[Collection("IntegrationTest")]
public class AgentRunMcpServiceTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private const string WebAppUrl = "https://boards.example.com";

    internal static IAgentRunMcpService CreateAgentRunMcpService(IServiceProvider services)
    {
        var database = services.GetRequiredService<DatabaseContext>();

        return new AgentRunMcpService(
            database,
            services.GetRequiredService<IAccessService>(),
            services.GetRequiredService<ICoreAgentRunsService>(),
            new IssueUrlBuilder(Options.Create(new WebAppOptions { Url = WebAppUrl })),
            new IssueLinksReader(database),
            new MemberProfileReader(database));
    }

    private static OrganizationAuthData AuthDataFor(long organizationId, Guid userId, Guid? apiKeyId = null)
    {
        return new OrganizationAuthData { OrganizationId = organizationId, UserId = userId, ApiKeyId = apiKeyId };
    }

    [Fact]
    public async Task ClaimAgentRun_ShouldReturnTrueForExactlyOneCaller_WhenTwoRunnersClaimInParallel()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var apiKeys = testScope.Services.GetRequiredService<ICoreApiKeysService>();
        var firstKey = await apiKeys.CreateAsync(organization.Id, ownerId, "Runner 1", CancellationToken.None);
        var secondKey = await apiKeys.CreateAsync(organization.Id, ownerId, "Runner 2", CancellationToken.None);
        var run = await CreateAgentRunMcpService(testScope.Services)
            .CreateAgentRun(AuthDataFor(organization.Id, ownerId), issueKey, CancellationToken.None);

        // Each runner has its own scope (own DbContext and connection), like two separate MCP requests. Not
        // CreateTestScope - that would clean the database.
        using var firstScope = host.Services.CreateScope();
        using var secondScope = host.Services.CreateScope();
        var claims = await Task.WhenAll(
            Task.Run(() => CreateAgentRunMcpService(firstScope.ServiceProvider)
                .ClaimAgentRun(AuthDataFor(organization.Id, ownerId, firstKey.Id), run.Id, CancellationToken.None)),
            Task.Run(() => CreateAgentRunMcpService(secondScope.ServiceProvider)
                .ClaimAgentRun(AuthDataFor(organization.Id, ownerId, secondKey.Id), run.Id, CancellationToken.None)));

        Assert.Single(claims, claimed => claimed);
        Assert.Single(claims, claimed => !claimed);

        var winnerKeyId = claims[0] ? firstKey.Id : secondKey.Id;
        var storedRun = await testScope.Database.IssueAgentRuns.SingleAsyncEF(x => x.Id == run.Id);
        Assert.Equal(AgentRunState.Working, storedRun.State);
        Assert.Equal(winnerKeyId, storedRun.ClaimedByApiKeyId);
        Assert.NotNull(storedRun.ClaimedAt);
    }

    [Fact]
    public async Task ClaimAgentRun_ShouldReturnFalse_WhenRunWasAlreadyClaimed()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var authData = AuthDataFor(organization.Id, ownerId);
        var service = CreateAgentRunMcpService(testScope.Services);
        var run = await service.CreateAgentRun(authData, issueKey, CancellationToken.None);

        Assert.True(await service.ClaimAgentRun(authData, run.Id, CancellationToken.None));
        Assert.False(await service.ClaimAgentRun(authData, run.Id, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAgentRun_ShouldReject_WhenIssueAlreadyHasOpenRun()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var authData = AuthDataFor(organization.Id, ownerId);
        var service = CreateAgentRunMcpService(testScope.Services);
        var run = await service.CreateAgentRun(authData, issueKey, CancellationToken.None);

        // Ready is open...
        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAgentRun(authData, issueKey, CancellationToken.None));

        // ...and so is Working.
        await service.ClaimAgentRun(authData, run.Id, CancellationToken.None);
        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAgentRun(authData, issueKey, CancellationToken.None));

        Assert.Equal(1, await testScope.Database.IssueAgentRuns.CountAsync());
    }

    [Fact]
    public async Task CreateAgentRun_ShouldRejectOneOfTwo_WhenTwoHandOffsRunInParallel()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var authData = AuthDataFor(organization.Id, ownerId);

        using var firstScope = host.Services.CreateScope();
        using var secondScope = host.Services.CreateScope();
        var results = await Task.WhenAll(
            CaptureException(() => CreateAgentRunMcpService(firstScope.ServiceProvider)
                .CreateAgentRun(authData, issueKey, CancellationToken.None)),
            CaptureException(() => CreateAgentRunMcpService(secondScope.ServiceProvider)
                .CreateAgentRun(authData, issueKey, CancellationToken.None)));

        Assert.Single(results, ex => ex is null);
        Assert.Single(results, ex => ex is BadRequestException);
        Assert.Equal(1, await testScope.Database.IssueAgentRuns.CountAsync());
    }

    [Fact]
    public async Task CreateAgentRun_ShouldCreateNewRun_WhenPreviousRunWasCancelled()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var authData = AuthDataFor(organization.Id, ownerId);
        var service = CreateAgentRunMcpService(testScope.Services);
        var firstRun = await service.CreateAgentRun(authData, issueKey, CancellationToken.None);

        Assert.True(await service.CancelAgentRun(authData, firstRun.Id, CancellationToken.None));
        var secondRun = await service.CreateAgentRun(authData, issueKey, CancellationToken.None);

        Assert.NotEqual(firstRun.Id, secondRun.Id);
        Assert.Equal("Ready", secondRun.State);
    }

    [Fact]
    public async Task ListAgentRuns_ShouldReturnOnlyHandedOffIssues_WhenFilteredByReady()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Handed off"))
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Claimed"))
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Not handed off")));
        var handedOffKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var claimedKey = organization.GetIssueData(1, 1, 0, 1).Key;
        var authData = AuthDataFor(organization.Id, ownerId);
        var service = CreateAgentRunMcpService(testScope.Services);
        var readyRun = await service.CreateAgentRun(authData, handedOffKey, CancellationToken.None);
        var claimedRun = await service.CreateAgentRun(authData, claimedKey, CancellationToken.None);
        await service.ClaimAgentRun(authData, claimedRun.Id, CancellationToken.None);

        var page = await service.ListAgentRuns(authData, null, AgentRunState.Ready, null, false, null, null, CancellationToken.None);

        var run = Assert.Single(page.Runs);
        Assert.Equal(readyRun.Id, run.Id);
        Assert.Equal(handedOffKey, run.IssueKey);
        Assert.Equal("Ready", run.State);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public async Task ListAgentRuns_ShouldNotReturnRunsOfUnreadableIssues_WhenCallerHasNoAccess()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId)
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        await CreateAgentRunMcpService(testScope.Services)
            .CreateAgentRun(AuthDataFor(organization.Id, ownerId), issueKey, CancellationToken.None);

        var page = await CreateAgentRunMcpService(testScope.Services)
            .ListAgentRuns(AuthDataFor(organization.Id, memberId), null, AgentRunState.Ready, null, false, null, null, CancellationToken.None);

        Assert.Empty(page.Runs);
    }

    [Fact]
    public async Task ListAgentRuns_ShouldReturnOnlyRunsOfGivenAssignees_WhenFilteredByAssignee()
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
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Owner's issue"))
            .AddIssueToAgentBoard(memberId, issue => issue.WithContent("Member's issue")));
        var ownerIssueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var memberIssueKey = organization.GetIssueData(1, 1, 0, 1).Key;
        var authData = AuthDataFor(organization.Id, ownerId);
        var service = CreateAgentRunMcpService(testScope.Services);
        await service.CreateAgentRun(authData, ownerIssueKey, CancellationToken.None);
        await service.CreateAgentRun(AuthDataFor(organization.Id, memberId), memberIssueKey, CancellationToken.None);

        var ownerRuns = await service.ListAgentRuns(authData, null, AgentRunState.Ready, [ownerId], false, null, null, CancellationToken.None);
        var bothRuns = await service.ListAgentRuns(authData, null, AgentRunState.Ready, [ownerId, memberId], false, null, null, CancellationToken.None);

        Assert.Equal([ownerIssueKey], ownerRuns.Runs.Select(r => r.IssueKey));
        Assert.Equal([ownerIssueKey, memberIssueKey], bothRuns.Runs.Select(r => r.IssueKey));
    }

    [Fact]
    public async Task ListAgentRuns_ShouldLeaveOutBlockedRunUntilBlockerIsCompleted_WhenExcludingBlocked()
    {
        using var testScope = host.CreateTestScope();
        var setup = await CreateBlockedIssueSetup(testScope);
        var service = CreateAgentRunMcpService(testScope.Services);
        var run = await service.CreateAgentRun(setup.AuthData, setup.BlockedKey, CancellationToken.None);

        Assert.True(run.IsBlocked);
        Assert.Empty((await service.ListAgentRuns(setup.AuthData, null, AgentRunState.Ready, null, true, null, null, CancellationToken.None)).Runs);
        var allRuns = await service.ListAgentRuns(setup.AuthData, null, AgentRunState.Ready, null, false, null, null, CancellationToken.None);
        Assert.True(Assert.Single(allRuns.Runs).IsBlocked);

        await CompleteBlocker(testScope, setup);

        var readyRun = Assert.Single(
            (await service.ListAgentRuns(setup.AuthData, null, AgentRunState.Ready, null, true, null, null, CancellationToken.None)).Runs);
        Assert.Equal(run.Id, readyRun.Id);
        Assert.False(readyRun.IsBlocked);
    }

    [Fact]
    public async Task ClaimAgentRun_ShouldReturnFalseUntilBlockerIsCompleted_WhenIssueIsBlocked()
    {
        using var testScope = host.CreateTestScope();
        var setup = await CreateBlockedIssueSetup(testScope);
        var service = CreateAgentRunMcpService(testScope.Services);
        var run = await service.CreateAgentRun(setup.AuthData, setup.BlockedKey, CancellationToken.None);

        Assert.False(await service.ClaimAgentRun(setup.AuthData, run.Id, CancellationToken.None));
        var storedRun = await testScope.Database.IssueAgentRuns.SingleAsyncEF(x => x.Id == run.Id);
        Assert.Equal(AgentRunState.Ready, storedRun.State);

        await CompleteBlocker(testScope, setup);

        Assert.True(await service.ClaimAgentRun(setup.AuthData, run.Id, CancellationToken.None));
    }

    private sealed record BlockedIssueSetup(
        OrganizationAuthData AuthData,
        string BlockerKey,
        string BlockedKey,
        long DoneStatusId);

    // Two issues in a board with a Completed "Done" status; the second one is blocked by the first.
    private static async Task<BlockedIssueSetup> CreateBlockedIssueSetup(WebApiTestHostScope testScope)
    {
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, space => space
                .AddEpic(ownerId, epic => epic
                    .AddStatus(s => s.WithName("Done").WithCategory(StatusCategory.Completed))
                    .AddStatus(s => s.WithName("In Progress").WithCategory(StatusCategory.InProgress))
                    .AddStatus(s => s.WithName("Review").WithCategory(StatusCategory.Review))
                    .AddIssue(ownerId, 0, issue => issue.WithContent("Blocker"))
                    .AddIssue(ownerId, 0, issue => issue.WithContent("Blocked")))));
        var blocker = organization.GetIssueData(1, 1, 0, 0);
        var blocked = organization.GetIssueData(1, 1, 0, 1);

        await using (var transaction = await testScope.Database.Database.BeginTransactionAsync())
        {
            await testScope.Services.GetRequiredService<ICoreIssueLinksService>()
                .AddLink(blocked.Issue.Id, IssueLinkRelation.BlockedBy, blocker.Issue.Id, ownerId, CancellationToken.None);
            await transaction.CommitAsync();
        }

        return new BlockedIssueSetup(
            AuthDataFor(organization.Id, ownerId),
            blocker.Key,
            blocked.Key,
            organization.GetStatus(1, 1, 1).Id);
    }

    private static Task CompleteBlocker(WebApiTestHostScope testScope, BlockedIssueSetup setup)
    {
        return IssueMcpServiceTests.CreateIssueMcpService(testScope)
            .EditIssueStatus(setup.AuthData, setup.BlockerKey, setup.DoneStatusId, comment: null, CancellationToken.None);
    }

    [Fact]
    public async Task CreateAgentRun_ShouldThrowNotFound_WhenIssueKeyDoesNotExist()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateAgentRunMcpService(testScope.Services)
            .CreateAgentRun(AuthDataFor(organization.Id, ownerId), "AGT-999", CancellationToken.None));
    }

    [Fact]
    public async Task CreateAgentRun_ShouldThrowNotFound_WhenCallerCannotReadIssue()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId)
            .AddIssueToAgentBoard(memberId, issue => issue.WithContent("Assigned to a member who can't read it")));

        await Assert.ThrowsAsync<NotFoundException>(() => CreateAgentRunMcpService(testScope.Services)
            .CreateAgentRun(AuthDataFor(organization.Id, memberId), organization.GetIssueData(1, 1, 0, 0).Key, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAgentRun_ShouldThrowForbidden_WhenCallerIsNotAssignee()
    {
        using var testScope = host.CreateTestScope();
        var setup = await CreateOwnerIssueWithEditorSetup(testScope);

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateAgentRunMcpService(testScope.Services)
            .CreateAgentRun(setup.EditorAuthData, setup.IssueKey, CancellationToken.None));
        Assert.Equal(0, await testScope.Database.IssueAgentRuns.CountAsync());
    }

    [Fact]
    public async Task ClaimAgentRun_ShouldThrowForbidden_WhenCallerIsNotAssignee()
    {
        using var testScope = host.CreateTestScope();
        var setup = await CreateOwnerIssueWithEditorSetup(testScope);
        var service = CreateAgentRunMcpService(testScope.Services);
        var run = await service.CreateAgentRun(setup.OwnerAuthData, setup.IssueKey, CancellationToken.None);

        await Assert.ThrowsAsync<ForbiddenException>(() => service
            .ClaimAgentRun(setup.EditorAuthData, run.Id, CancellationToken.None));
        var storedRun = await testScope.Database.IssueAgentRuns.SingleAsyncEF(x => x.Id == run.Id);
        Assert.Equal(AgentRunState.Ready, storedRun.State);
    }

    [Fact]
    public async Task CancelAgentRun_ShouldCancel_WhenCallerCanEditButIsNotAssignee()
    {
        using var testScope = host.CreateTestScope();
        var setup = await CreateOwnerIssueWithEditorSetup(testScope);
        var service = CreateAgentRunMcpService(testScope.Services);
        var run = await service.CreateAgentRun(setup.OwnerAuthData, setup.IssueKey, CancellationToken.None);

        Assert.True(await service.CancelAgentRun(setup.EditorAuthData, run.Id, CancellationToken.None));
    }

    [Fact]
    public async Task EditIssue_ShouldThrowConflictUntilRunIsCancelled_WhenIssueIsHandedOff()
    {
        using var testScope = host.CreateTestScope();
        var setup = await CreateOwnerIssueWithEditorSetup(testScope);
        var service = CreateAgentRunMcpService(testScope.Services);
        var issueMcpService = IssueMcpServiceTests.CreateIssueMcpService(testScope);
        var run = await service.CreateAgentRun(setup.OwnerAuthData, setup.IssueKey, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<IssueLockedByAgentRunException>(() => issueMcpService.EditIssue(
            setup.EditorAuthData, setup.IssueKey, "Do something else", "Title", null, null, null, null, CancellationToken.None));
        Assert.Equal(System.Net.HttpStatusCode.Conflict, ex.StatusCode);
        await Assert.ThrowsAsync<IssueLockedByAgentRunException>(() => issueMcpService.DeleteIssue(
            setup.OwnerAuthData, setup.IssueKey, CancellationToken.None));

        await service.CancelAgentRun(setup.EditorAuthData, run.Id, CancellationToken.None);
        await issueMcpService.EditIssue(
            setup.EditorAuthData, setup.IssueKey, "Do something else", "Title", null, null, null, null, CancellationToken.None);

        var issue = await issueMcpService.GetIssue(setup.OwnerAuthData, setup.IssueKey, CancellationToken.None);
        Assert.Equal("Do something else", issue.Content);
    }

    [Fact]
    public async Task CreateComment_ShouldWork_WhenIssueIsHandedOff()
    {
        using var testScope = host.CreateTestScope();
        var setup = await CreateOwnerIssueWithEditorSetup(testScope);
        await CreateAgentRunMcpService(testScope.Services)
            .CreateAgentRun(setup.OwnerAuthData, setup.IssueKey, CancellationToken.None);

        var commentId = await IssueMcpServiceTests.CreateIssueMcpService(testScope)
            .CreateComment(setup.EditorAuthData, setup.IssueKey, "Note for the agent", CancellationToken.None);

        Assert.True(commentId > 0);
    }

    private sealed record OwnerIssueWithEditorSetup(
        OrganizationAuthData OwnerAuthData,
        OrganizationAuthData EditorAuthData,
        string IssueKey);

    // An issue assigned to the owner, and a member who can edit it but isn't its assignee.
    private static async Task<OwnerIssueWithEditorSetup> CreateOwnerIssueWithEditorSetup(WebApiTestHostScope testScope)
    {
        var ownerId = await testScope.CreateUser();
        var editorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(editorId, builder => builder.SetGlobalAccessLevel(x =>
            {
                x.CanRead = true;
                x.CanUpdateIssues = true;
            }))
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));

        return new OwnerIssueWithEditorSetup(
            AuthDataFor(organization.Id, ownerId),
            AuthDataFor(organization.Id, editorId),
            organization.GetIssueData(1, 1, 0, 0).Key);
    }

    [Fact]
    public async Task CreateAgentRun_ShouldReject_WhenBoardHasNoInProgressOrReviewStatus()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("In the backlog")));

        var ex = await Assert.ThrowsAsync<BadRequestException>(() => CreateAgentRunMcpService(testScope.Services)
            .CreateAgentRun(AuthDataFor(organization.Id, ownerId), organization.GetIssueData(0, 0, 0, 0).Key, CancellationToken.None));

        Assert.Contains("InProgress, Review", ex.Message + string.Join(" ", ex.Errors?.SelectMany(e => e.Value) ?? []));
        Assert.Equal(0, await testScope.Database.IssueAgentRuns.CountAsync());
    }

    [Fact]
    public async Task CreateAgentRun_ShouldReject_WhenIssueIsAlreadyStarted()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        var authData = AuthDataFor(organization.Id, ownerId);
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        await IssueMcpServiceTests.CreateIssueMcpService(testScope)
            .EditIssueStatus(authData, issueKey, organization.GetStatus(1, 1, 1).Id, comment: null, CancellationToken.None);

        await Assert.ThrowsAsync<BadRequestException>(() => CreateAgentRunMcpService(testScope.Services)
            .CreateAgentRun(authData, issueKey, CancellationToken.None));
    }

    [Fact]
    public async Task ClaimAgentRun_ShouldFailRun_WhenIssueMovedToBoardWithoutReview()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        var authData = AuthDataFor(organization.Id, ownerId);
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var service = CreateAgentRunMcpService(testScope.Services);
        var run = await service.CreateAgentRun(authData, issueKey, CancellationToken.None);

        // Status changes stay allowed while the issue is handed off - here, to the default space's backlog.
        await IssueMcpServiceTests.CreateIssueMcpService(testScope)
            .EditIssueStatus(authData, issueKey, organization.GetStatus(0, 0, 0).Id, comment: null, CancellationToken.None);

        Assert.False(await service.ClaimAgentRun(authData, run.Id, CancellationToken.None));

        var storedRun = await testScope.Database.IssueAgentRuns.SingleAsyncEF(x => x.Id == run.Id);
        Assert.Equal(AgentRunState.Failed, storedRun.State);
        Assert.Contains("InProgress, Review", storedRun.FailureReason);
    }

    [Fact]
    public async Task FinishAgentRun_ShouldStoreFailureReason_WhenRunFailed()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var authData = AuthDataFor(organization.Id, ownerId);
        var service = CreateAgentRunMcpService(testScope.Services);
        var run = await service.CreateAgentRun(authData, issueKey, CancellationToken.None);
        await service.ClaimAgentRun(authData, run.Id, CancellationToken.None);

        var finished = await service.FinishAgentRun(authData, run.Id, AgentRunFinishState.Failed, "Build is red", CancellationToken.None);

        Assert.True(finished);
        var latest = await service.GetLatestRun(authData, organization.GetIssueData(1, 1, 0, 0).Issue.Id, CancellationToken.None);
        Assert.NotNull(latest);
        Assert.Equal("Failed", latest.State);
        Assert.Equal("Build is red", latest.FailureReason);
        Assert.NotNull(latest.FinishedAt);
    }

    [Fact]
    public async Task FinishAgentRun_ShouldReturnFalse_WhenRunWasCancelled()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var authData = AuthDataFor(organization.Id, ownerId);
        var service = CreateAgentRunMcpService(testScope.Services);
        var run = await service.CreateAgentRun(authData, issueKey, CancellationToken.None);
        await service.ClaimAgentRun(authData, run.Id, CancellationToken.None);
        await service.CancelAgentRun(authData, run.Id, CancellationToken.None);

        var finished = await service.FinishAgentRun(authData, run.Id, AgentRunFinishState.Done, null, CancellationToken.None);

        Assert.False(finished);
        var storedRun = await testScope.Database.IssueAgentRuns.SingleAsyncEF(x => x.Id == run.Id);
        Assert.Equal(AgentRunState.Cancelled, storedRun.State);
    }

    [Fact]
    public async Task CancelAgentRun_ShouldReturnFalse_WhenRunAlreadyFinished()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var authData = AuthDataFor(organization.Id, ownerId);
        var service = CreateAgentRunMcpService(testScope.Services);
        var run = await service.CreateAgentRun(authData, issueKey, CancellationToken.None);
        await service.ClaimAgentRun(authData, run.Id, CancellationToken.None);
        await service.FinishAgentRun(authData, run.Id, AgentRunFinishState.Done, null, CancellationToken.None);

        Assert.False(await service.CancelAgentRun(authData, run.Id, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAgentRun_ShouldThrowForbidden_WhenCallerCanOnlyReadIssue()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder.SetGlobalAccessLevel(x => x.CanRead = true))
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateAgentRunMcpService(testScope.Services)
            .CreateAgentRun(AuthDataFor(organization.Id, memberId), issueKey, CancellationToken.None));
    }

    [Fact]
    public async Task ClaimAgentRun_ShouldThrowNotFound_WhenCallerCannotReadIssue()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId)
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var run = await CreateAgentRunMcpService(testScope.Services)
            .CreateAgentRun(AuthDataFor(organization.Id, ownerId), issueKey, CancellationToken.None);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateAgentRunMcpService(testScope.Services)
            .ClaimAgentRun(AuthDataFor(organization.Id, memberId), run.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ClaimAgentRun_ShouldWriteHistoryWithApiKey_WhenRunnerClaims()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var apiKey = await testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(organization.Id, ownerId, "Claude Code agent", CancellationToken.None);
        var service = CreateAgentRunMcpService(testScope.Services);
        var run = await service.CreateAgentRun(AuthDataFor(organization.Id, ownerId), issueKey, CancellationToken.None);

        await service.ClaimAgentRun(AuthDataFor(organization.Id, ownerId, apiKey.Id), run.Id, CancellationToken.None);

        var history = await IssueMcpServiceTests.CreateIssueMcpService(testScope)
            .GetIssueHistory(AuthDataFor(organization.Id, ownerId), issueKey, null, null, CancellationToken.None);
        Assert.Collection(
            history.Entries,
            claim =>
            {
                Assert.Equal("Claude Code agent", claim.ApiKeyName);
                Assert.Equal([$"agent run {run.Id}: Ready -> Working"], claim.Changes);
            },
            handOff =>
            {
                Assert.Null(handOff.ApiKeyName);
                Assert.Equal([$"agent run {run.Id}: none -> Ready"], handOff.Changes);
            });
    }

    [Fact]
    public async Task GetIssue_ShouldReturnLatestAgentRun_WhenIssueWasHandedOff()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var authData = AuthDataFor(organization.Id, ownerId);
        var issueMcpService = IssueMcpServiceTests.CreateIssueMcpService(testScope);
        var service = CreateAgentRunMcpService(testScope.Services);

        Assert.Null((await issueMcpService.GetIssue(authData, issueKey, CancellationToken.None)).LatestAgentRun);

        var firstRun = await service.CreateAgentRun(authData, issueKey, CancellationToken.None);
        await service.CancelAgentRun(authData, firstRun.Id, CancellationToken.None);
        var secondRun = await service.CreateAgentRun(authData, issueKey, CancellationToken.None);

        var latest = (await issueMcpService.GetIssue(authData, issueKey, CancellationToken.None)).LatestAgentRun;
        Assert.NotNull(latest);
        Assert.Equal(secondRun.Id, latest.Id);
        Assert.Equal("Ready", latest.State);
    }

    private static async Task<Exception?> CaptureException(Func<Task> action)
    {
        try
        {
            await Task.Run(action);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
