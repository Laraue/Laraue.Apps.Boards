using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.McpHost.Services;
using Laraue.Apps.Boards.Services;
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
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
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
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
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
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
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
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
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
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
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
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Handed off"))
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Claimed"))
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Not handed off")));
        var handedOffKey = organization.GetIssueData(0, 0, 0, 0).Key;
        var claimedKey = organization.GetIssueData(0, 0, 0, 1).Key;
        var authData = AuthDataFor(organization.Id, ownerId);
        var service = CreateAgentRunMcpService(testScope.Services);
        var readyRun = await service.CreateAgentRun(authData, handedOffKey, CancellationToken.None);
        var claimedRun = await service.CreateAgentRun(authData, claimedKey, CancellationToken.None);
        await service.ClaimAgentRun(authData, claimedRun.Id, CancellationToken.None);

        var page = await service.ListAgentRuns(authData, null, "Ready", null, null, CancellationToken.None);

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
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
        await CreateAgentRunMcpService(testScope.Services)
            .CreateAgentRun(AuthDataFor(organization.Id, ownerId), issueKey, CancellationToken.None);

        var page = await CreateAgentRunMcpService(testScope.Services)
            .ListAgentRuns(AuthDataFor(organization.Id, memberId), null, "Ready", null, null, CancellationToken.None);

        Assert.Empty(page.Runs);
    }

    [Fact]
    public async Task ListAgentRuns_ShouldReject_WhenStateIsUnknown()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);

        await Assert.ThrowsAsync<BadRequestException>(() => CreateAgentRunMcpService(testScope.Services)
            .ListAgentRuns(AuthDataFor(organization.Id, ownerId), null, "Sleeping", null, null, CancellationToken.None));
    }

    [Fact]
    public async Task FinishAgentRun_ShouldStoreFailureReason_WhenRunFailed()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
        var authData = AuthDataFor(organization.Id, ownerId);
        var service = CreateAgentRunMcpService(testScope.Services);
        var run = await service.CreateAgentRun(authData, issueKey, CancellationToken.None);
        await service.ClaimAgentRun(authData, run.Id, CancellationToken.None);

        var finished = await service.FinishAgentRun(authData, run.Id, "Failed", "Build is red", CancellationToken.None);

        Assert.True(finished);
        var latest = await service.GetLatestRun(organization.Id, organization.GetIssueData(0, 0, 0, 0).Issue.Id, CancellationToken.None);
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
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
        var authData = AuthDataFor(organization.Id, ownerId);
        var service = CreateAgentRunMcpService(testScope.Services);
        var run = await service.CreateAgentRun(authData, issueKey, CancellationToken.None);
        await service.ClaimAgentRun(authData, run.Id, CancellationToken.None);
        await service.CancelAgentRun(authData, run.Id, CancellationToken.None);

        var finished = await service.FinishAgentRun(authData, run.Id, "Done", null, CancellationToken.None);

        Assert.False(finished);
        var storedRun = await testScope.Database.IssueAgentRuns.SingleAsyncEF(x => x.Id == run.Id);
        Assert.Equal(AgentRunState.Cancelled, storedRun.State);
    }

    [Fact]
    public async Task FinishAgentRun_ShouldReject_WhenStateIsNotDoneOrFailed()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
        var authData = AuthDataFor(organization.Id, ownerId);
        var service = CreateAgentRunMcpService(testScope.Services);
        var run = await service.CreateAgentRun(authData, issueKey, CancellationToken.None);

        await Assert.ThrowsAsync<BadRequestException>(() => service.FinishAgentRun(authData, run.Id, "Cancelled", null, CancellationToken.None));
    }

    [Fact]
    public async Task CancelAgentRun_ShouldReturnFalse_WhenRunAlreadyFinished()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
        var authData = AuthDataFor(organization.Id, ownerId);
        var service = CreateAgentRunMcpService(testScope.Services);
        var run = await service.CreateAgentRun(authData, issueKey, CancellationToken.None);
        await service.ClaimAgentRun(authData, run.Id, CancellationToken.None);
        await service.FinishAgentRun(authData, run.Id, "Done", null, CancellationToken.None);

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
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;

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
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
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
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
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
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
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
