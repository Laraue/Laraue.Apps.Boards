using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// <see cref="Issue.CreatedVia"/> is set on create from how the issue was created, and the web issue, the board column
/// and the list return it with the creating key's name (BRD-325). Telegram is covered in
/// <see cref="TelegramMessageSourceTests"/>.
/// </summary>
[Collection("IntegrationTest")]
public class IssueCreatedViaTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<IssuesController> _issuesController = host.Controller<IssuesController>();

    [Fact]
    public async Task Create_ShouldSetCreatedViaWeb_WhenCreatedInWebApp()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var status = organization.GetStatus(0, 0, 0);

        var issueKey = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(new CreateIssueRequest
            {
                Title = "Title",
                Content = "From the web",
                StatusId = status.Id,
                AssigneeId = userId,
            }));

        await AssertCreatedVia(organization.Id, userId, status.Id, issueKey!, IssueCreatedVia.Web, expectedKeyName: null);
    }

    [Fact]
    public async Task CreateIssue_ShouldSetCreatedViaMcpWithKeyName_WhenCreatedWithHandMadeKey()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var status = organization.GetStatus(0, 0, 0);

        var key = await testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(organization.Id, userId, "My laptop", CancellationToken.None);

        var issueKey = await IssueMcpServiceTests.CreateIssueMcpService(testScope).CreateIssue(
            IssueMcpServiceTests.AuthDataFor(organization.Id, userId, key.Id),
            "From MCP",
            "Title",
            status.Id,
            null,
            null,
            null,
            CancellationToken.None);

        await AssertCreatedVia(organization.Id, userId, status.Id, issueKey, IssueCreatedVia.Mcp, "My laptop");
    }

    [Fact]
    public async Task CreateIssue_ShouldSetCreatedViaAgentWithKeyName_WhenCreatedWithAgentSignInKey()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var status = organization.GetStatus(0, 0, 0);

        var key = await testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(organization.Id, userId, "Claude Code agent (dev-laptop)", CancellationToken.None);
        testScope.Database.Add(new AgentSignInRequest
        {
            DeviceCodeHash = Guid.NewGuid().ToString("N"),
            UserCode = Guid.NewGuid().ToString("N")[..9],
            ClientName = "Claude Code agent",
            MachineName = "dev-laptop",
            State = AgentSignInState.Completed,
            UserId = userId,
            OrganizationId = organization.Id,
            ApiKeyId = key.Id,
        });
        await testScope.Database.SaveChangesAsync();

        var issueKey = await IssueMcpServiceTests.CreateIssueMcpService(testScope).CreateIssue(
            IssueMcpServiceTests.AuthDataFor(organization.Id, userId, key.Id),
            "From the agent",
            "Title",
            status.Id,
            null,
            null,
            null,
            CancellationToken.None);

        await AssertCreatedVia(
            organization.Id,
            userId,
            status.Id,
            issueKey,
            IssueCreatedVia.Agent,
            "Claude Code agent (dev-laptop)");
    }

    private async Task AssertCreatedVia(
        long organizationId,
        Guid userId,
        long statusId,
        string issueKey,
        IssueCreatedVia expected,
        string? expectedKeyName)
    {
        var issue = await _issuesController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.GetIssue(issueKey));

        Assert.Equal(expected, issue!.CreatedVia);
        Assert.Equal(expectedKeyName, issue.CreatedByApiKeyName);

        var column = await _issuesController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.GetIssuesByStatus(statusId, new GetIssuesRequest { Take = 20 }));

        var row = Assert.Single(column!.Data);
        Assert.Equal(expected, row.CreatedVia);
        Assert.Equal(expectedKeyName, row.CreatedByApiKeyName);
    }
}
