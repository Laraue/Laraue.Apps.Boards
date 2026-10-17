using System.Net;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services.History;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DataAccess.Contracts;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class AgentRunsControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<AgentRunsController> _agentRunsController = host.Controller<AgentRunsController>();
    private readonly Proxy<IssuesController> _issuesController = host.Controller<IssuesController>();

    [Fact]
    public async Task Create_ShouldShowReadyRunOnBoardCard_WhenIssueIsHandedOff()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, o => o
            .AddIssueToDefaultStatus(userId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;

        var run = await _agentRunsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(new CreateAgentRunRequest { IssueKey = issueKey }));

        Assert.Equal(AgentRunState.Ready, run.State);

        var columns = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetBoard(new GetBoardRequest { EpicId = organization.GetEpic(0, 0).Id, Take = 10 }));

        var card = Assert.Single(Assert.Single(columns).Items.Data);
        Assert.Equal(new IssueAgentRunShortDto { Id = run.Id, State = AgentRunState.Ready }, card.LatestAgentRun);
    }

    [Fact]
    public async Task Cancel_ShouldCancelRun_WhenRunIsOpen()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, o => o
            .AddIssueToDefaultStatus(userId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
        var controller = _agentRunsController.WithOrganizationAuthorization(organization.Id, userId);
        var run = await controller.Execute(x => x.Create(new CreateAgentRunRequest { IssueKey = issueKey }));

        var cancelled = await controller.Execute(x => x.Cancel(run.Id));
        var cancelledAgain = await controller.Execute(x => x.Cancel(run.Id));

        Assert.True(cancelled);
        Assert.False(cancelledAgain);

        var issue = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetIssue(issueKey));
        Assert.Equal(AgentRunState.Cancelled, issue.LatestAgentRun?.State);
    }

    [Fact]
    public async Task Create_ShouldReturnBadRequest_WhenIssueAlreadyHasOpenRun()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, o => o
            .AddIssueToDefaultStatus(userId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
        var controller = _agentRunsController.WithOrganizationAuthorization(organization.Id, userId);
        await controller.Execute(x => x.Create(new CreateAgentRunRequest { IssueKey = issueKey }));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => controller
            .Execute(x => x.Create(new CreateAgentRunRequest { IssueKey = issueKey })));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task Create_ShouldShowRunInIssueHistory_WhenIssueIsHandedOff()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, o => o
            .AddIssueToDefaultStatus(userId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
        var run = await _agentRunsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(new CreateAgentRunRequest { IssueKey = issueKey }));

        var history = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetIssueHistory(issueKey, new GetIssueHistoryRequest { Pagination = new PaginationData { Page = 0, PerPage = 10 } }));

        var change = Assert.IsType<IssueHistoryAgentRunChange>(Assert.Single(history.Data[0].Changes));
        Assert.Equal(run.Id, change.RunId);
        Assert.Null(change.OldState);
        Assert.Equal(AgentRunState.Ready, change.NewState);
    }
}
