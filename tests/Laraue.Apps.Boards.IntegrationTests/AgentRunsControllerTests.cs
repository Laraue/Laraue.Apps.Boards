using System.Net;
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
            .AddIssueToAgentBoard(userId, issue => issue.WithContent("Fix the thing")));
        await CreateApiKey(testScope, organization.Id, userId);
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;

        var run = await _agentRunsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(new CreateAgentRunRequest { IssueKey = issueKey }));

        Assert.Equal(AgentRunState.Ready, run.State);

        var columns = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetBoard(new GetBoardRequest { EpicId = organization.GetEpic(1, 1).Id, Take = 10 }));

        var card = Assert.Single(columns.SelectMany(x => x.Items.Data));
        Assert.Equal(new IssueAgentRunShortDto { Id = run.Id, State = AgentRunState.Ready }, card.LatestAgentRun);
    }

    [Fact]
    public async Task Cancel_ShouldCancelRun_WhenRunIsOpen()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, o => o
            .AddIssueToAgentBoard(userId, issue => issue.WithContent("Fix the thing")));
        await CreateApiKey(testScope, organization.Id, userId);
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
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
            .AddIssueToAgentBoard(userId, issue => issue.WithContent("Fix the thing")));
        await CreateApiKey(testScope, organization.Id, userId);
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
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
            .AddIssueToAgentBoard(userId, issue => issue.WithContent("Fix the thing")));
        await CreateApiKey(testScope, organization.Id, userId);
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
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

    [Fact]
    public async Task Create_ShouldReturnBadRequest_WhenCallerHasNoApiKey()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, o => o
            .AddIssueToAgentBoard(userId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var controller = _agentRunsController.WithOrganizationAuthorization(organization.Id, userId);

        var issue = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetIssue(issueKey));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => controller
            .Execute(x => x.Create(new CreateAgentRunRequest { IssueKey = issueKey })));

        Assert.Equal(AgentHandOffBlocker.NoApiKey, issue.AgentHandOff.Blocker);
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task GetIssue_ShouldOfferHandOff_WhenAssigneeWithApiKeyHasNotStartedIssueOnAgentBoard()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, o => o
            .AddIssueToAgentBoard(userId, issue => issue.WithContent("Fix the thing")));
        await CreateApiKey(testScope, organization.Id, userId);

        var issue = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetIssue(organization.GetIssueData(1, 1, 0, 0).Key));

        Assert.True(issue.AgentHandOff.Available);
        Assert.Null(issue.AgentHandOff.Blocker);
    }

    [Fact]
    public async Task GetIssue_ShouldNotAllowHandOff_WhenCallerIsNotAssignee()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var editorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, o => o
            .AddUser(editorId, builder => builder.SetGlobalAccessLevel(x =>
            {
                x.CanRead = true;
                x.CanUpdateIssues = true;
            }))
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        await CreateApiKey(testScope, organization.Id, editorId);

        var issue = await _issuesController
            .WithOrganizationAuthorization(organization.Id, editorId)
            .Execute(x => x.GetIssue(organization.GetIssueData(1, 1, 0, 0).Key));

        Assert.False(issue.AgentHandOff.Available);
        Assert.Equal(AgentHandOffBlocker.NotAllowed, issue.AgentHandOff.Blocker);
    }

    [Fact]
    public async Task GetIssue_ShouldNameMissingStatuses_WhenBoardIsNotReadyForAgent()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, o => o
            .AddIssueToDefaultStatus(userId, issue => issue.WithContent("In the backlog")));
        await CreateApiKey(testScope, organization.Id, userId);

        var issue = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetIssue(organization.GetIssueData(0, 0, 0, 0).Key));

        Assert.Equal(AgentHandOffBlocker.BoardNotReady, issue.AgentHandOff.Blocker);
        Assert.Equal([StatusCategory.InProgress, StatusCategory.Review], issue.AgentHandOff.MissingStatusCategories);
    }

    [Fact]
    public async Task GetIssue_ShouldReportStartedIssue_WhenIssueIsInProgress()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, o => o
            .AddIssueToAgentBoard(userId, issue => issue.WithContent("Fix the thing")));
        await CreateApiKey(testScope, organization.Id, userId);
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        var issues = _issuesController.WithOrganizationAuthorization(organization.Id, userId);
        var moveToInProgress = new UpdateIssuesStatusRequest
        {
            IssueKeys = [issueKey],
            StatusId = organization.GetStatus(1, 1, 1).Id,
        };
        await issues.Execute(x => x.UpdateStatus(moveToInProgress));

        var issue = await issues.Execute(x => x.GetIssue(issueKey));

        Assert.Equal(AgentHandOffBlocker.IssueStarted, issue.AgentHandOff.Blocker);
    }

    [Fact]
    public async Task GetIssue_ShouldReportOpenRun_WhenIssueIsAlreadyHandedOff()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, o => o
            .AddIssueToAgentBoard(userId, issue => issue.WithContent("Fix the thing")));
        await CreateApiKey(testScope, organization.Id, userId);
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        await _agentRunsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(new CreateAgentRunRequest { IssueKey = issueKey }));

        var issue = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetIssue(issueKey));

        Assert.Equal(AgentHandOffBlocker.RunOpen, issue.AgentHandOff.Blocker);
    }

    [Fact]
    public async Task GetIssue_ShouldOfferSendBack_WhenAgentFinishedRunAndIssueIsInReview()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, o => o
            .AddIssueToAgentBoard(userId, issue => issue.WithContent("Fix the thing")));
        await CreateApiKey(testScope, organization.Id, userId);
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        await HandOffAndFinishInReview(testScope, organization, userId, issueKey);

        var issue = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetIssue(issueKey));

        Assert.Equal(AgentHandOffBlocker.IssueStarted, issue.AgentHandOff.Blocker);
        Assert.True(issue.AgentHandOff.CanSendBack);
    }

    [Fact]
    public async Task SendBack_ShouldPostCommentMoveIssueBackAndHandItOff_WhenAgentFinishedRun()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, o => o
            .AddIssueToAgentBoard(userId, issue => issue.WithContent("Fix the thing")));
        await CreateApiKey(testScope, organization.Id, userId);
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        await HandOffAndFinishInReview(testScope, organization, userId, issueKey);

        var run = await _agentRunsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.SendBack(new SendBackAgentRunRequest { IssueKey = issueKey, Comment = "  The button is still blue.  " }));

        Assert.Equal(AgentRunState.Ready, run.State);
        var issues = _issuesController.WithOrganizationAuthorization(organization.Id, userId);
        var issue = await issues.Execute(x => x.GetIssue(issueKey));
        Assert.Equal(StatusCategory.Created, issue.StatusCategory);
        Assert.Equal(run.Id, issue.LatestAgentRun?.Id);
        Assert.False(issue.AgentHandOff.CanSendBack);
        var comments = await issues.Execute(x => x.GetIssueComments(
            issueKey,
            new GetIssueCommentsRequest { Pagination = new PaginationData { Page = 0, PerPage = 10 } }));
        Assert.Equal("The button is still blue.", Assert.Single(comments.Data).Text);
    }

    [Fact]
    public async Task SendBack_ShouldReturnBadRequest_WhenAgentNeverWorkedOnIssue()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, o => o
            .AddIssueToAgentBoard(userId, issue => issue.WithContent("Fix the thing")));
        await CreateApiKey(testScope, organization.Id, userId);
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _agentRunsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.SendBack(new SendBackAgentRunRequest { IssueKey = issueKey, Comment = "Again" })));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task SendBack_ShouldReturnForbidden_WhenCallerIsNotAssignee()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var editorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, o => o
            .AddUser(editorId, builder => builder.SetGlobalAccessLevel(x =>
            {
                x.CanRead = true;
                x.CanUpdateIssues = true;
            }))
            .AddIssueToAgentBoard(ownerId, issue => issue.WithContent("Fix the thing")));
        await CreateApiKey(testScope, organization.Id, ownerId);
        await CreateApiKey(testScope, organization.Id, editorId);
        var issueKey = organization.GetIssueData(1, 1, 0, 0).Key;
        await HandOffAndFinishInReview(testScope, organization, ownerId, issueKey);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _agentRunsController
            .WithOrganizationAuthorization(organization.Id, editorId)
            .Execute(x => x.SendBack(new SendBackAgentRunRequest { IssueKey = issueKey, Comment = "Again" })));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    // Hands the issue off, lets "the agent" claim and finish the run as Done, and moves the issue to Review - where an
    // agent leaves it.
    private async Task HandOffAndFinishInReview(
        WebApiTestHostScope testScope,
        Organization organization,
        Guid userId,
        string issueKey)
    {
        var run = await _agentRunsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(new CreateAgentRunRequest { IssueKey = issueKey }));

        var core = testScope.Services.GetRequiredService<ICoreAgentRunsService>();
        await using (var transaction = await testScope.Database.Database.BeginTransactionAsync())
        {
            Assert.True(await core.Claim(run.Id, userId, CancellationToken.None));
            Assert.True(await core.Finish(run.Id, AgentRunState.Done, null, userId, CancellationToken.None));
            await transaction.CommitAsync();
        }

        var moveToReview = new UpdateIssuesStatusRequest
        {
            IssueKeys = [issueKey],
            StatusId = organization.GetStatus(1, 1, 2).Id,
        };
        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.UpdateStatus(moveToReview));
    }

    // The agent runner signs in with one of the user's API keys, so a hand-off needs one.
    private static Task CreateApiKey(WebApiTestHostScope testScope, long organizationId, Guid userId)
    {
        return testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(organizationId, userId, "boards-agent", CancellationToken.None);
    }
}
