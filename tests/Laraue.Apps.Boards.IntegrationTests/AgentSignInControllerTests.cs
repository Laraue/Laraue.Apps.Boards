using System.Net;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class AgentSignInControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<AgentSignInController> _controller = host.Controller<AgentSignInController>();

    [Fact]
    public async Task Start_ShouldReturnCodesAndAllowPage_WhenCalledAnonymously()
    {
        var started = await StartAsync();

        Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", started.UserCode);
        Assert.Equal($"/agent-sign-in?code={started.UserCode}", started.VerificationPath);
        Assert.True(started.DeviceCode.Length >= 32);
        Assert.Equal(5, started.IntervalSeconds);
        Assert.True(started.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Get_ShouldShowMachineAndKeyName_WhenSignInIsPending()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var started = await StartAsync("Ilya-MacBook");

        var signIn = await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.Get(started.UserCode.ToLowerInvariant(), CancellationToken.None));

        Assert.Equal(started.UserCode, signIn.UserCode);
        Assert.Equal("Ilya-MacBook", signIn.MachineName);
        Assert.Equal("Claude Code agent (Ilya-MacBook)", signIn.KeyName);
    }

    [Fact]
    public async Task Get_ShouldReturnNotFound_WhenCodeIsUnknown()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.Get("AAAA-BBBB", CancellationToken.None)));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Poll_ShouldReturnPendingThenSlowDown_WhenNobodyAnsweredAndRunnerPollsTooSoon()
    {
        var started = await StartAsync();

        var first = await PollAsync(started.DeviceCode);
        var second = await PollAsync(started.DeviceCode);

        Assert.Equal(AgentSignInPollOutcome.Pending, first.Outcome);
        Assert.Null(first.ApiKey);
        Assert.Equal(AgentSignInPollOutcome.SlowDown, second.Outcome);
    }

    [Fact]
    public async Task Poll_ShouldReturnWorkingKeyOnce_WhenUserApproved()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var started = await StartAsync("test-mac");

        await ApproveAsync(userId, started.UserCode, organization.Id);
        var approved = await PollAsync(started.DeviceCode);
        var again = await PollAsync(started.DeviceCode);

        Assert.Equal(AgentSignInPollOutcome.Approved, approved.Outcome);
        Assert.StartsWith("brdk_", approved.ApiKey);
        Assert.Equal(organization.Id, approved.OrganizationId);
        Assert.Equal(organization.Name, approved.OrganizationName);
        Assert.Equal(userId, approved.UserId);
        Assert.Equal(AgentSignInPollOutcome.Expired, again.Outcome);
        Assert.Null(again.ApiKey);

        var principal = await testScope.Services
            .GetRequiredService<ICoreApiKeysService>()
            .ValidateAsync(approved.ApiKey!, CancellationToken.None);
        Assert.NotNull(principal);
        Assert.Equal(organization.Id, principal.OrganizationId);
        Assert.Equal(userId, principal.UserId);

        var key = await testScope.Database.ApiKeys.SingleAsync(x => x.Id == approved.ApiKeyId);
        Assert.Equal("Claude Code agent (test-mac)", key.Name);
    }

    [Fact]
    public async Task Poll_ShouldRevokeEarlierKeyOfSameMachine_WhenMachineSignsInAgain()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        var firstKey = await SignInAsync(userId, organization.Id, "same-mac");
        var secondKey = await SignInAsync(userId, organization.Id, "same-mac");

        var coreApiKeysService = testScope.Services.GetRequiredService<ICoreApiKeysService>();
        Assert.Null(await coreApiKeysService.ValidateAsync(firstKey, CancellationToken.None));
        Assert.NotNull(await coreApiKeysService.ValidateAsync(secondKey, CancellationToken.None));
    }

    [Fact]
    public async Task Poll_ShouldKeepKeyOfOtherMachine_WhenAnotherMachineSignsIn()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        var laptopKey = await SignInAsync(userId, organization.Id, "laptop");
        await SignInAsync(userId, organization.Id, "desktop");

        var coreApiKeysService = testScope.Services.GetRequiredService<ICoreApiKeysService>();
        Assert.NotNull(await coreApiKeysService.ValidateAsync(laptopKey, CancellationToken.None));
    }

    [Fact]
    public async Task Poll_ShouldReturnDenied_WhenUserDenied()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var started = await StartAsync();

        await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.Deny(started.UserCode, CancellationToken.None));
        var poll = await PollAsync(started.DeviceCode);

        Assert.Equal(AgentSignInPollOutcome.Denied, poll.Outcome);
        Assert.Null(poll.ApiKey);
    }

    [Fact]
    public async Task Poll_ShouldReturnExpired_WhenSignInTimedOut()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var started = await StartAsync();
        await ExpireAsync(testScope, started.UserCode);

        var poll = await PollAsync(started.DeviceCode);
        var approveError = await Assert.ThrowsAsync<HttpRequestException>(
            () => ApproveAsync(userId, started.UserCode, organization.Id));

        Assert.Equal(AgentSignInPollOutcome.Expired, poll.Outcome);
        Assert.Equal(HttpStatusCode.NotFound, approveError.StatusCode);
    }

    [Fact]
    public async Task Poll_ShouldReturnNotFound_WhenDeviceCodeIsUnknown()
    {
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => PollAsync("unknown-device-code"));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Approve_ShouldReturnNotFound_WhenUserIsNotMemberOfOrganization()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var strangerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);
        var started = await StartAsync();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => ApproveAsync(strangerId, started.UserCode, organization.Id));
        var poll = await PollAsync(started.DeviceCode);

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal(AgentSignInPollOutcome.Pending, poll.Outcome);
    }

    [Fact]
    public async Task Approve_ShouldReturnNotFound_WhenSignInWasAlreadyAnswered()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var started = await StartAsync();
        await ApproveAsync(userId, started.UserCode, organization.Id);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => ApproveAsync(userId, started.UserCode, organization.Id));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Start_ShouldReturnBadRequest_WhenMachineNameIsTooLong()
    {
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => StartAsync(new string('m', 45)));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    private Task<StartAgentSignInResponse> StartAsync(string machineName = "test-machine")
    {
        return _controller.Execute(x => x.Start(
            new StartAgentSignInRequest { MachineName = machineName },
            CancellationToken.None));
    }

    private Task<PollAgentSignInResponse> PollAsync(string deviceCode)
    {
        return _controller.Execute(x => x.Poll(
            new PollAgentSignInRequest { DeviceCode = deviceCode },
            CancellationToken.None));
    }

    private Task ApproveAsync(Guid userId, string userCode, long organizationId)
    {
        return _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.Approve(
                userCode,
                new ApproveAgentSignInRequest { OrganizationId = organizationId },
                CancellationToken.None));
    }

    /// <summary>
    /// The whole flow - start, allow, poll - returning the issued raw key.
    /// </summary>
    private async Task<string> SignInAsync(Guid userId, long organizationId, string machineName)
    {
        var started = await StartAsync(machineName);
        await ApproveAsync(userId, started.UserCode, organizationId);
        var poll = await PollAsync(started.DeviceCode);
        return poll.ApiKey!;
    }

    private static Task ExpireAsync(WebApiTestHostScope testScope, string userCode)
    {
        return testScope.Database.AgentSignInRequests
            .Where(x => x.UserCode == userCode)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
    }
}
