using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.History;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Comments and history made with a key issued by agent sign-in read as the agent's, with its client and machine;
/// the app and keys made by hand read as the person's, as before (BRD-325).
/// </summary>
[Collection("IntegrationTest")]
public class AuthorKindTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<IssuesController> _issuesController = host.Controller<IssuesController>();

    [Fact]
    public async Task GetIssueComments_ShouldReturnUserAuthor_WhenCommentWasWrittenInApp()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Setup(testScope);

        await _issuesController
            .WithOrganizationAuthorization(setup.OrganizationId, setup.UserId)
            .Execute(x => x.AddComment(new AddCommentRequest { Text = "From the app", IssueKey = setup.IssueKey }));

        var comment = Assert.Single(await GetComments(setup));
        Assert.Equal(AuthorKind.User, comment.AuthorKind);
        Assert.Null(comment.Agent);
        Assert.Null(comment.ApiKeyName);
    }

    [Fact]
    public async Task GetIssueComments_ShouldReturnUserAuthorWithKeyName_WhenCommentWasWrittenWithHandMadeKey()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Setup(testScope);
        var keyId = await CreateKey(setup, "My laptop", agent: null);

        await IssueMcpServiceTests.CreateIssueMcpService(setup.TestScope).CreateComment(
            IssueMcpServiceTests.AuthDataFor(setup.OrganizationId, setup.UserId, keyId),
            setup.IssueKey,
            "From MCP",
            null,
            CancellationToken.None);

        var comment = Assert.Single(await GetComments(setup));
        Assert.Equal(AuthorKind.User, comment.AuthorKind);
        Assert.Null(comment.Agent);
        Assert.Equal("My laptop", comment.ApiKeyName);
    }

    [Fact]
    public async Task GetIssueComments_ShouldReturnAgentAuthor_WhenCommentWasWrittenWithAgentSignInKey()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Setup(testScope);
        var keyId = await CreateKey(setup, "Claude Code agent (dev-laptop)", ("Claude Code agent", "dev-laptop"));

        await IssueMcpServiceTests.CreateIssueMcpService(setup.TestScope).CreateComment(
            IssueMcpServiceTests.AuthDataFor(setup.OrganizationId, setup.UserId, keyId),
            setup.IssueKey,
            "Started work",
            null,
            CancellationToken.None);

        var comment = Assert.Single(await GetComments(setup));
        Assert.Equal(AuthorKind.Agent, comment.AuthorKind);
        Assert.Equal(new AgentDetails { ClientName = "Claude Code agent", MachineName = "dev-laptop" }, comment.Agent);
        Assert.Equal(setup.UserId, comment.Owner.UserId);
    }

    [Fact]
    public async Task GetIssueHistory_ShouldReturnAgentAuthorOnlyForAgentChanges_WhenAgentAndPersonChangedIssue()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Setup(testScope);
        var keyId = await CreateKey(setup, "Claude Code agent (dev-laptop)", ("Claude Code agent", "dev-laptop"));

        await IssueMcpServiceTests.CreateIssueMcpService(setup.TestScope).CreateComment(
            IssueMcpServiceTests.AuthDataFor(setup.OrganizationId, setup.UserId, keyId),
            setup.IssueKey,
            "Started work",
            null,
            CancellationToken.None);
        await _issuesController
            .WithOrganizationAuthorization(setup.OrganizationId, setup.UserId)
            .Execute(x => x.AddComment(new AddCommentRequest { Text = "Thanks", IssueKey = setup.IssueKey }));

        var history = await _issuesController
            .WithOrganizationAuthorization(setup.OrganizationId, setup.UserId)
            .Execute(x => x.GetIssueHistory(
                setup.IssueKey,
                new GetIssueHistoryRequest { Pagination = new PaginationData { Page = 0, PerPage = 10 } }));

        // Newest first: the person's comment, then the agent's.
        Assert.Collection(
            history!.Data,
            item =>
            {
                Assert.Equal(AuthorKind.User, item.AuthorKind);
                Assert.Null(item.Agent);
            },
            item =>
            {
                Assert.Equal(AuthorKind.Agent, item.AuthorKind);
                Assert.Equal("dev-laptop", item.Agent?.MachineName);
                Assert.Equal("Claude Code agent (dev-laptop)", item.ApiKeyName);
            });
    }

    private sealed record TestSetup(WebApiTestHostScope TestScope, long OrganizationId, Guid UserId, string IssueKey);

    private static async Task<TestSetup> Setup(WebApiTestHostScope testScope)
    {
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, org => org
            .AddIssueToDefaultStatus(userId));

        return new TestSetup(testScope, organization.Id, userId, organization.GetIssueData(0, 0, 0, 0).Key);
    }

    private static async Task<Guid> CreateKey(
        TestSetup setup,
        string name,
        (string ClientName, string MachineName)? agent)
    {
        var key = await setup.TestScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(setup.OrganizationId, setup.UserId, name, CancellationToken.None);

        if (agent is { } a)
        {
            setup.TestScope.Database.Add(new AgentSignInRequest
            {
                DeviceCodeHash = Guid.NewGuid().ToString("N"),
                UserCode = Guid.NewGuid().ToString("N")[..9],
                ClientName = a.ClientName,
                MachineName = a.MachineName,
                State = AgentSignInState.Completed,
                UserId = setup.UserId,
                OrganizationId = setup.OrganizationId,
                ApiKeyId = key.Id,
            });
            await setup.TestScope.Database.SaveChangesAsync();
        }

        return key.Id;
    }

    private async Task<IList<CommentDto>> GetComments(TestSetup setup)
    {
        var comments = await _issuesController
            .WithOrganizationAuthorization(setup.OrganizationId, setup.UserId)
            .Execute(x => x.GetIssueComments(
                setup.IssueKey,
                new GetIssueCommentsRequest { Pagination = new PaginationData { Page = 0, PerPage = 10 } }));

        return comments!.Data.ToList();
    }
}
