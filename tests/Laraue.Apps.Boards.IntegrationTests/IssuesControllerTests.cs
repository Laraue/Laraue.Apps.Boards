using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class IssuesControllerTests(WebApiTestHost host)  : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<IssuesController> _issuesController = host.Controller<IssuesController>();
    
    [Fact]
    public async Task User_ShouldCreateIssue_WhenIsOrganizationOwner()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(x => x.TelegramUserName = "user1");
        var organization = await testScope.InitializeOrganization(userId);

        var status = organization.GetStatus(0, 0, 0);
        
        var issueKey = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(
                new CreateIssueRequest
                {
                    Content = "New Issue",
                    StatusId = status.Id,
                    AssigneeId = userId,
                }));

        var issue = await testScope.Database.FindIssueByKey(organization.Id, issueKey!);
        Assert.NotNull(issue);
        Assert.Equal("New Issue", issue.Content);
        
        var issueNumber = await testScope.Database.IssueNumbers.FirstAsyncEF(e => e.IssueId == issue.Id);
        Assert.Equal(1, issueNumber.Number);
        
        var historyChange = await testScope.Database.IssueUpdates.Include(x => x.Items).SingleAsyncEF();
        Assert.Equal(issue.Id, historyChange.IssueId);
        Assert.Equal(4, historyChange.Items!.Count);

        var issueChange = historyChange.Items[0];
        var statusChange = historyChange.Items[1];
        var contentChange = historyChange.Items[2];
        var assigneeChange = historyChange.Items[3];
        
        Assert.Null(issueChange.OldDisplayValue);
        Assert.Equal(issueKey, issueChange.NewDisplayValue);
        Assert.Null(issueChange.PropertyName);
        Assert.Null(issueChange.OldValueId);
        Assert.Null(issueChange.NewValueId);
        Assert.Null(issueChange.PropertyName);
        Assert.Equal(ChangeAction.Create, issueChange.Action);
        Assert.Equal(IssueUpdateEntityType.Issue, issueChange.EntityType);
        
        Assert.Null(statusChange.OldDisplayValue);
        Assert.Equal(status.Name, statusChange.NewDisplayValue);
        Assert.Null(statusChange.PropertyName);
        Assert.Null(statusChange.OldValueId);
        Assert.Equal(status.Id.ToString(), statusChange.NewValueId);
        Assert.Null(statusChange.PropertyName);
        Assert.Equal(ChangeAction.Update, statusChange.Action);
        Assert.Equal(IssueUpdateEntityType.Status, statusChange.EntityType);
        
        Assert.Null(contentChange.OldDisplayValue);
        Assert.Equal("New Issue", contentChange.NewDisplayValue);
        Assert.Null(contentChange.PropertyName);
        Assert.Null(contentChange.OldValueId);
        Assert.Null(contentChange.NewValueId);
        Assert.Null(contentChange.PropertyName);
        Assert.Equal(ChangeAction.Update, contentChange.Action);
        Assert.Equal(IssueUpdateEntityType.Content, contentChange.EntityType);
        
        Assert.Null(assigneeChange.OldDisplayValue);
        Assert.Equal("user1", assigneeChange.NewDisplayValue);
        Assert.Null(assigneeChange.PropertyName);
        Assert.Null(assigneeChange.OldValueId);
        Assert.Equal(userId.ToString(), assigneeChange.NewValueId);
        Assert.Null(assigneeChange.PropertyName);
        Assert.Equal(ChangeAction.Update, assigneeChange.Action);
        Assert.Equal(IssueUpdateEntityType.Assignee, assigneeChange.EntityType);
    }
    
    [Fact]
    public async Task User_ShouldNotCreateIssue_WhenHasNoAccess()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            organization => organization.AddUser(participatorId));

        var status = organization.GetStatus(0, 0, 0);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Create(
                new CreateIssueRequest
                {
                    Content = "New Issue",
                    StatusId = status.Id,
                    AssigneeId = userId,
                })));
        
        var notFound = ex.HasInnerException<NotFoundException>();
        Assert.Equal($"Status: {status.Id} is not found", notFound.Message);
    }
    
    [Fact]
    public async Task User_ShouldCreateIssue_WhenHasGlobalAccessToCreateIssues()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            organization => organization
                .AddUser(participatorId, u => u
                    .SetGlobalAccessLevel(x => x.CanCreateIssues = true)));

        var status = organization.GetStatus(0, 0, 0);
        
        var issueId = await _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Create(
                new CreateIssueRequest
                {
                    Content = "New Issue",
                    StatusId = status.Id,
                    AssigneeId = userId,
                }));

        var issue = await testScope.Database.FindIssueByKey(organization.Id, issueId!);
        Assert.NotNull(issue);
        Assert.Equal("New Issue", issue.Content);
        Assert.Equal(userId, issue.AssigneeId);
    }
    
    [Fact]
    public async Task User_ShouldCreateIssue_WhenHasIssuesAccessOnSpaceLevel()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            organization => organization
                .AddUser(participatorId, u => u
                    .SetSpaceAccessLevel(0, x => x.CanCreateIssues = true)));

        var status = organization.GetStatus(0, 0, 0);
        
        var issueId = await _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Create(
                new CreateIssueRequest
                {
                    Content = "New Issue",
                    StatusId = status.Id,
                    AssigneeId = participatorId,
                }));

        var issue = await testScope.Database.FindIssueByKey(organization.Id, issueId!);
        Assert.NotNull(issue);
        Assert.Equal("New Issue", issue.Content);
        Assert.Equal(participatorId, issue.AssigneeId);
    }
    
    [Fact]
    public async Task User_ShouldNotCreateIssue_WhenHasIssuesAccessOnOtherSpaceLevel()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            organization => organization
                .AddSpace(userId)
                .AddUser(participatorId, u => u
                    .SetSpaceAccessLevel(0, x => x.CanCreateIssues = true)));

        var statusWhereSpaceAccessMissing = organization.GetStatus(1, 0, 0);
        
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Create(
                new CreateIssueRequest
                {
                    Content = "New Issue",
                    StatusId = statusWhereSpaceAccessMissing.Id,
                    AssigneeId = participatorId,
                })));
        
        var notFound = ex.HasInnerException<NotFoundException>();
        Assert.Equal($"Status: {statusWhereSpaceAccessMissing.Id} is not found", notFound.Message);
    }
    
    [Fact]
    public async Task User_ShouldUpdateIssue_WhenIsOrganizationOwner()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddIssueToDefaultStatus(userId, builder => builder.WithContent("Hi")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        
        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Update(
                issueData.Key,
                new UpdateIssueRequest
                {
                    Content = "New",
                    AttributeValues = Array.Empty<AttributeValue>(),
                    AssigneeId = userId,
                }));

        var issue = await testScope.Database.FindIssueByKey(organization.Id, issueData.Key);
        Assert.NotNull(issue);
        Assert.Equal("New", issue.Content);
    }
    
    [Fact]
    public async Task User_ShouldNotUpdateIssue_WhenHasNotAccess()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId, u => u
                    .SetGlobalAccessLevel( x => x.CanCreateIssues = true))
                .AddIssueToDefaultStatus(userId, builder => builder.WithContent("Hi")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Update(
                issueData.Key,
                new UpdateIssueRequest
                {
                    Content = "New",
                    AttributeValues = Array.Empty<AttributeValue>(),
                    AssigneeId = userId,
                })));
        
        var notFound = ex.HasInnerException<ForbiddenException>();
        Assert.Equal($"Issue: {issueData.Key} update is forbidden", notFound.Message);
    }
    
    [Fact]
    public async Task User_ShouldUpdateIssue_WhenHasGlobalAccessToUpdateIssues()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(u => u.TelegramUserName = "first_user");
        var participatorId = await testScope.CreateUser(u => u.TelegramUserName = "second_user");
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId, u => u
                    .SetGlobalAccessLevel( x => x.CanUpdateIssues = true))
                .AddIssueToDefaultStatus(userId, builder => builder.WithContent("Hi")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        
        await _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Update(
                issueData.Key,
                new UpdateIssueRequest
                {
                    Content = "New",
                    AttributeValues = Array.Empty<AttributeValue>(),
                    AssigneeId = participatorId,
                }));

        var issue = await testScope.Database.FindIssueByKey(organization.Id, issueData.Key);
        Assert.NotNull(issue);
        Assert.Equal("New", issue.Content);

        var historyChange = await testScope.Database.IssueUpdates.Include(x => x.Items).SingleAsyncEF();
        Assert.Equal(issue.Id, historyChange.IssueId);
        Assert.Equal(2, historyChange.Items!.Count);

        var contentChange = historyChange.Items[0];
        var assigneeChange = historyChange.Items[1];
        
        Assert.Equal("Hi", contentChange.OldDisplayValue);
        Assert.Equal("New", contentChange.NewDisplayValue);
        Assert.Null(contentChange.PropertyName);
        Assert.Null(contentChange.OldValueId);
        Assert.Null(contentChange.NewValueId);
        Assert.Null(contentChange.PropertyName);
        Assert.Equal(ChangeAction.Update, contentChange.Action);
        Assert.Equal(IssueUpdateEntityType.Content, contentChange.EntityType);
        
        Assert.Equal("first_user", assigneeChange.OldDisplayValue);
        Assert.Equal("second_user", assigneeChange.NewDisplayValue);
        Assert.Null(assigneeChange.PropertyName);
        Assert.Equal(userId.ToString(), assigneeChange.OldValueId);
        Assert.Equal(participatorId.ToString(), assigneeChange.NewValueId);
        Assert.Null(assigneeChange.PropertyName);
        Assert.Equal(ChangeAction.Update, assigneeChange.Action);
        Assert.Equal(IssueUpdateEntityType.Assignee, assigneeChange.EntityType);
    }
    
    [Fact]
    public async Task User_ShouldUpdateIssue_WhenHasAccessToUpdateIssuesOnSpaceLevel()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId, u => u
                    .SetSpaceAccessLevel(0, x => x.CanUpdateIssues = true))
                .AddIssueToDefaultStatus(userId, builder => builder.WithContent("Hi")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        
        await _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Update(
                issueData.Key,
                new UpdateIssueRequest
                {
                    Content = "New",
                    AttributeValues = Array.Empty<AttributeValue>(),
                    AssigneeId = participatorId,
                }));

        var issue = await testScope.Database.FindIssueByKey(organization.Id, issueData.Key);
        Assert.NotNull(issue);
        Assert.Equal("New", issue.Content);
        Assert.Equal(participatorId, issue.AssigneeId);
    }
    
    [Fact]
    public async Task User_ShouldDeleteIssue_WhenIsOrganizationOwner()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddIssueToDefaultStatus(userId));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        
        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Delete(issueData.Key));

        var issue = await testScope.Database.FindIssueByKey(organization.Id, issueData.Key);
        Assert.Null(issue);
        
        var historyChange = await testScope.Database.IssueUpdates.Include(x => x.Items).SingleAsyncEF();
        Assert.Equal(issueData.Issue.Id, historyChange.IssueId);
        var issueChange = Assert.Single(historyChange.Items!);
        
        Assert.Equal(issueData.Key, issueChange.OldDisplayValue);
        Assert.Null(issueChange.NewDisplayValue);
        Assert.Null(issueChange.PropertyName);
        Assert.Null(issueChange.OldValueId);
        Assert.Null(issueChange.NewValueId);
        Assert.Null(issueChange.PropertyName);
        Assert.Equal(ChangeAction.Delete, issueChange.Action);
        Assert.Equal(IssueUpdateEntityType.Issue, issueChange.EntityType);
    }
    
    [Fact]
    public async Task User_ShouldNotDeleteIssue_WhenHasNotAccess()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId,  u => u
                    .SetGlobalAccessLevel( x => x.CanCreateIssues = true))
                .AddIssueToDefaultStatus(userId, builder => builder.WithContent("Hi")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Delete(issueData.Key)));
        
        var notFound = ex.HasInnerException<ForbiddenException>();
        Assert.Equal($"Issue: {issueData.Key} delete is forbidden", notFound.Message);
    }
    
    [Fact]
    public async Task User_ShouldDeleteIssue_WhenHasGlobalAccessToDeleteIssues()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId,  u => u
                    .SetGlobalAccessLevel( x => x.CanDeleteIssues = true))
                .AddIssueToDefaultStatus(userId));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        
        await _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Delete(issueData.Key));

        var issue = await testScope.Database.FindIssueByKey(organization.Id, issueData.Key);
        Assert.Null(issue);
    }

    [Fact]
    public async Task User_ShouldDeleteIssue_WhenHasAccessToDeleteIssuesOnSpaceLevel()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId,  u => u
                    .SetSpaceAccessLevel(0, x => x.CanDeleteIssues = true))
                .AddIssueToDefaultStatus(userId));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        
        await _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Delete(issueData.Key));

        var issue = await testScope.Database.FindIssueByKey(organization.Id, issueData.Key);
        Assert.Null(issue);
    }

    [Fact]
    public async Task User_ShouldViewIssue_WhenIsOrganizationOwner()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(user => user.TelegramUserName = "assignee");
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o.AddIssueToDefaultStatus(userId));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        
        var issueDto = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetIssue(issueData.Key));

        var space = organization.GetSpace(0);
        Assert.NotNull(issueDto);
        Assert.Equal(userId, issueDto.AssigneeId);
        Assert.Equal("assignee", issueDto.Assignee.DisplayName);
        Assert.Equal("as", issueDto.Assignee.Initials);
        Assert.Equal(space.Key, issueDto.SpaceKey);
        Assert.Equal(space.Name, issueDto.SpaceName);
    }
    
    [Fact]
    public async Task User_ShouldNotViewIssue_WhenHasNotPermissions()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId)
                .AddIssueToDefaultStatus(userId));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.GetIssue(issueData.Key)));
        
        var notFound = ex.HasInnerException<NotFoundException>();
        Assert.Equal($"Issue: {issueData.Key} is not found or not accessible", notFound.Message);
    }

    [Fact]
    public async Task User_ShouldViewIssue_WhenHasGlobalAccessToReadIssues()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId, u => u
                    .SetGlobalAccessLevel( x => x.CanRead = true))
                .AddIssueToDefaultStatus(userId));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        
        var issueDto = await _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.GetIssue(issueData.Key));
        
        Assert.NotNull(issueDto);
    }

    [Fact]
    public async Task User_ShouldViewIssue_WhenHasSpaceAccessToReadIssues()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId, u => u
                    .SetSpaceAccessLevel(0, x => x.CanRead = true))
                .AddIssueToDefaultStatus(userId));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        
        var issueDto = await _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.GetIssue(issueData.Key));
        
        Assert.NotNull(issueDto);
    }

    [Fact]
    public async Task User_ShouldSearchAllIssues_WhenHasIssuesAccessOnGlobalLevel()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId, u => u
                    .SetGlobalAccessLevel(x => x.CanRead = true))
                .AddIssueToDefaultStatus(userId, issue => issue.WithContent("Hi"))
                .AddIssueToDefaultStatus(userId, issue => issue.WithContent("John")));
        
        var issuesResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    SearchString = "jo",
                    Page = 0,
                    PerPage = 10,
                }));
        
        Assert.NotNull(issuesResult);
        var issueDto = Assert.Single(issuesResult.Data);
        Assert.Equal("John", issueDto.Content);
        Assert.False(issueDto.CanEdit);
    }
    
    [Fact]
    public async Task User_ShouldSearchOnlyPermittedSpaceIssues_WhenHasIssuesAccessOnSpaceLevel()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId, u => u
                    .SetSpaceAccessLevel(1, x => { x.CanRead = true; x.CanUpdateIssues = true; }))
                .AddIssueToDefaultStatus(userId, issue => issue.WithContent("John 1"))
                .AddSpace(userId, space => space
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, issue => issue.WithContent("John 2")))));
        
        var issuesResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    SearchString = "jo",
                    Page = 0,
                    PerPage = 10,
                }));
        
        Assert.NotNull(issuesResult);
        var issueDto = Assert.Single(issuesResult.Data);
        Assert.Equal("John 2", issueDto.Content);
        Assert.True(issueDto.CanEdit);
    }
    
    [Fact]
    public async Task User_ShouldGetBoard_WhenIsOrganizationOwner()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddIssueToDefaultStatus(userId, issue => issue.WithContent("John 1"))
                .AddIssueToDefaultStatus(userId, issue => issue.WithContent("John 2")));

        var epic = organization.GetEpic(0, 0);
        
        var boardColumns = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetBoard(
                new GetBoardRequest
                {
                    SearchString = "jo",
                    EpicId = epic.Id,
                    Take = 10
                }));
        
        Assert.NotNull(boardColumns);
        var boardColumn = Assert.Single(boardColumns);
        Assert.Equal(2, boardColumn.Items.TotalCount);
    }
    
    [Fact]
    public async Task User_ShouldNotGetBoard_WhenHasNotAccess()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddUser(participatorId)
                .AddIssueToDefaultStatus(userId, issue => issue.WithContent("John 1"))
                .AddIssueToDefaultStatus(userId, issue => issue.WithContent("John 2")));

        var epic = organization.GetEpic(0, 0);
        
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.GetBoard(
                new GetBoardRequest
                {
                    EpicId = epic.Id,
                    Take = 10
                })));
        
        var notFound = ex.HasInnerException<NotFoundException>();
        Assert.Equal($"Epic: {epic.Id} is not found", notFound.Message);
    }

    [Fact]
    public async Task User_ShouldMoveIssue_WhenIsOwner()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddSpace(userId, s => s
                    .AddEpic(userId, e => e.AddStatus()))
                .AddIssueToDefaultStatus(userId));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var newStatus = organization.GetStatus(1, 1, 1);

        var request = new UpdateIssuesStatusRequest { IssueKeys = [issueData.Key], StatusId = newStatus.Id };
        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.UpdateStatus(request));

        var issue = await testScope.Database.Issues.FirstAsyncEF(x => x.Id == issueData.Issue.Id);
        Assert.Equal(newStatus.Id, issue.StatusId);
    }

    [Fact]
    public async Task User_ShouldMoveIssue_WhenHasCreateIssuesAccessInEpicAndIssueUpdateAccess()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId, u => u
                    .SetGlobalAccessLevel(x => { x.CanCreateIssues = true; x.CanUpdateIssues = true; }))
                .AddSpace(userId, s => s
                    .AddEpic(userId, e => e.AddStatus()))
                .AddIssueToDefaultStatus(userId));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var newStatus = organization.GetStatus(1, 1, 1);
        
        var request = new UpdateIssuesStatusRequest { IssueKeys = [issueData.Key], StatusId = newStatus.Id };
        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.UpdateStatus(request));

        var issue = await testScope.Database.Issues.FirstAsyncEF(x => x.Id == issueData.Issue.Id);
        Assert.Equal(newStatus.Id, issue.StatusId);
    }

    [Fact]
    public async Task User_ShouldNotMoveIssue_WhenHasCreateIssuesAccessInEpicButIssueUpdateAccessMissing()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId, u => u
                    .SetGlobalAccessLevel(x => x.CanCreateIssues = true))
                .AddSpace(userId, s => s
                    .AddEpic(userId, e => e.AddStatus()))
                .AddIssueToDefaultStatus(userId));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var newStatus = organization.GetStatus(1, 1, 1);
        
        var request = new UpdateIssuesStatusRequest { IssueKeys = [issueData.Key], StatusId = newStatus.Id };
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.UpdateStatus(request)));
        
        var notFound = ex.HasInnerException<ForbiddenException>();
        Assert.Equal($"Issue: {issueData.Key} is not available for this action", notFound.Message);
    }

    [Fact]
    public async Task User_ShouldNotMoveIssue_WhenHasIssueUpdateAccessButCreateIssueAccessIsMissing()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId, u => u
                    .SetGlobalAccessLevel(x => x.CanUpdateIssues = true))
                .AddSpace(userId, s => s
                    .AddEpic(userId, e => e.AddStatus()))
                .AddIssueToDefaultStatus(userId));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var newStatus = organization.GetStatus(1, 1, 1);
        
        var request = new UpdateIssuesStatusRequest { IssueKeys = [issueData.Key], StatusId = newStatus.Id };
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.UpdateStatus(request)));
        
        var notFound = ex.HasInnerException<NotFoundException>();
        Assert.Equal($"Status: {newStatus.Id} is not found", notFound.Message);
    }
    
    
    [Fact]
    public async Task User_ShouldMovePersonalIssue_WhenStatusExists()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddSpace(userId, s => s
                    .AddEpic(userId, e => e
                        .AddStatus(st => st.WithName("Beautiful status"))
                        .AddIssue(userId, 0))));

        var issueData = organization.GetIssueData(1, 1, 0, 0);
        var newStatus = organization.GetStatus(1, 1, 1);
        
        var request = new UpdateIssuesStatusRequest { IssueKeys = [issueData.Key], StatusId = newStatus.Id };
        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.UpdateStatus(request));

        var issue = await testScope.Database.FindIssueByKey(organization.Id, issueData.Key);
        Assert.NotNull(issue);
        Assert.Equal(newStatus.Id, issue.StatusId);
    }
    
    [Fact]
    public async Task User_ShouldNotMovePersonalIssue_WhenStatusNotExists()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddSpace(userId, s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0))));

        var issueData = organization.GetIssueData(1, 1, 0, 0);

        var request = new UpdateIssuesStatusRequest { IssueKeys = [issueData.Key], StatusId = 0 };
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.UpdateStatus(request)));
        
        var notFoundException = ex.HasInnerException<NotFoundException>();
        Assert.Equal("Status: 0 is not found", notFoundException.Message);
    }
    
    [Fact]
    public async Task User_ShouldSeeIssueComments_WhenIssueAvailable()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(
                    participatorId,
                    permissions => permissions
                        .SetGlobalAccessLevel(l => l.CanRead = true))
                .AddIssueToDefaultStatus(userId, builder => builder
                    .AddComment(userId, "Comment 1")
                    .AddComment(participatorId, "Comment 2")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);

        var request = new GetIssueCommentsRequest
        {
            Pagination = new PaginationData
            {
                Page = 0,
                PerPage = 8,
            }
        };
        
        var commentsData = await _issuesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.GetIssueComments(issueData.Key, request));

        var data = commentsData!.Data;
        Assert.Equal(2, data.Count);

        var userComment = data[0];
        var participatorComment = data[1];
        
        Assert.False(userComment.CanModify);
        Assert.Equal("Comment 1", userComment.Text);
        
        Assert.True(participatorComment.CanModify);
        Assert.Equal("Comment 2", participatorComment.Text);
    }
    
    [Fact]
    public async Task User_ShouldSeeIssueHistory_WhenIssueAvailable()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(x => x.TelegramUserName = "user1");
        var participatorId = await testScope.CreateUser(x => x.TelegramUserName = "user2");
        var organization = await testScope.InitializeOrganization(
            userId,
            initializer => initializer
                .AddListAttribute("Type", ["Bug", "Feature"])
                .AddListAttribute("Urgency", ["Low", "High"])
                .AddTextAttribute("Note")
                .AddTextAttribute("Description")
                .AddIssueToDefaultStatus(participatorId, builder => builder
                    .AddAttachment("old.jpg", AttachmentType.Image)
                    .WithAttributeValue(0, 0) // Type = Bug
                    .WithAttributeValue(1, 0) // Urgency = Low
                    .WithAttributeValue(2, "Ask mr. John") // Note = Ask mr. John
                    .WithAttributeValue(3, "50 cents debt") // Description = 50 cents debt
                    .WithContent("Old")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var typeAttribute = organization.Attributes![0];
        var urgencyAttribute = organization.Attributes![1];
        var noteAttribute = organization.Attributes![2];
        var descriptionAttribute = organization.Attributes![3];
        
        var updateIssueRequest = new UpdateIssueRequest
        {
            AssigneeId = userId,
            Content = "New",
            AttributeValues =
            [
                new EnumAttributeValue // Type = Bug, unchanged
                {
                    ValueId = typeAttribute.AttributeListValues![0].Id,
                    AttributeId = typeAttribute.Id,
                },
                new EnumAttributeValue // Urgency = High, changed
                {
                    ValueId = urgencyAttribute.AttributeListValues![1].Id,
                    AttributeId = urgencyAttribute.Id,
                },
                new StringAttributeValue // Note, unchanged
                {
                    AttributeId = noteAttribute.Id,
                    Value = "Ask mr. John"
                },
                // Description deleted
            ],
            AddFiles =
            [
                FormFileUtility.GetFormFile("image.jpg"),
            ],
            RemoveAttachmentIds = [issueData.Issue.IssueAttachments![0].AttachmentId]
        };
        
        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Update(issueData.Key, updateIssueRequest));

        var request = new GetIssueHistoryRequest
        {
            Pagination = new PaginationData
            {
                Page = 0,
                PerPage = 8,
            }
        };
        
        var historyData = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetIssueHistory(issueData.Key!, request));

        var change = Assert.Single(historyData!.Data);
        Assert.Equal("user1", change.Owner.DisplayName);
        
        var itemChanges = change.Changes;
        Assert.Equal(6, itemChanges.Length);
        
        var contentChange = Assert.IsType<IssueHistoryContentChange>(itemChanges[0]);
        var assigneeChange = Assert.IsType<IssueHistoryAssigneeChange>(itemChanges[1]);
        var attachmentAddChange = Assert.IsType<IssueHistoryAttachmentChange>(itemChanges[2]);
        var attachmentDeleteChange = Assert.IsType<IssueHistoryAttachmentChange>(itemChanges[3]);
        var descriptionAttributeChange = Assert.IsType<IssueHistoryPropertyChange>(itemChanges[4]);
        var urgencyAttributeChange = Assert.IsType<IssueHistoryPropertyChange>(itemChanges[5]);
        
        Assert.Equal("Old", contentChange.OldContent);
        Assert.Equal("New", contentChange.NewContent);
        
        Assert.Equal("user2", assigneeChange.OldAssigneeDisplayName);
        Assert.Equal(participatorId, assigneeChange.OldAssigneeId);
        Assert.Equal("user1", assigneeChange.NewAssigneeDisplayName);
        Assert.Equal(userId, assigneeChange.NewAssigneeId);
        
        Assert.Equal("image.jpg", attachmentAddChange.FileName);
        Assert.True(attachmentAddChange.FileId != Guid.Empty);
        Assert.Equal(ChangeAction.Create, attachmentAddChange.ChangeAction);
        
        Assert.Equal("old.jpg", attachmentDeleteChange.FileName);
        Assert.Equal(issueData.Issue.IssueAttachments[0].Attachment!.FileId, attachmentDeleteChange.FileId);
        Assert.Equal(ChangeAction.Delete, attachmentDeleteChange.ChangeAction);
        
        Assert.Equal("Description", descriptionAttributeChange.PropertyName);
        Assert.Null(descriptionAttributeChange.NewValueName);
        Assert.Null(descriptionAttributeChange.NewValueId);
        Assert.Equal("50 cents debt", descriptionAttributeChange.OldValueName);
        Assert.Null(descriptionAttributeChange.OldValueId);
        
        Assert.Equal("Urgency", urgencyAttributeChange.PropertyName);
        Assert.Equal("High", urgencyAttributeChange.NewValueName);
        Assert.Equal(urgencyAttribute.AttributeListValues[1].Id,  urgencyAttributeChange.NewValueId);
        Assert.Equal("Low", urgencyAttributeChange.OldValueName);
        Assert.Equal(urgencyAttribute.AttributeListValues[0].Id,  urgencyAttributeChange.OldValueId);
    }
}
