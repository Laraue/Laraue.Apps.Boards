using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Ai;
using Laraue.Apps.Boards.Services.Sorting;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class IssuesControllerTests(WebApiTestHost host)  : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<IssuesController> _issuesController = host.Controller<IssuesController>();
    
    [Fact]
    public async Task User_ShouldCreateIssue_WhenIsOrganizationOwner()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(x => 
        {
            x.TelegramUserName = "user1";
            x.Color = "#000000";
        });
        var organization = await testScope.InitializeOrganization(userId);

        var status = organization.GetStatus(0, 0, 0);
        var space = organization.GetSpace(0);
        var epic = organization.GetEpic(0, 0);
        
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
        
        var historyChange = await testScope.Database.OrganizationLogs.Include(x => x.Items).SingleAsyncEF();
        Assert.Equal(issue.Id, historyChange.EntityId);
        Assert.Equal(LogEntityType.Issue, historyChange.EntityType);
        Assert.Equal(LogAction.Create, historyChange.Action);
        Assert.Equal(5, historyChange.Items!.Count);

        var spaceChange = historyChange.Items[0];
        var epicChange = historyChange.Items[1];
        var statusChange = historyChange.Items[2];
        var contentChange = historyChange.Items[3];
        var assigneeChange = historyChange.Items[4];
        
        Assert.Null(spaceChange.OldDisplayValue);
        Assert.Equal(space.Name, spaceChange.NewDisplayValue);
        Assert.Equal(space.Id.ToString(), spaceChange.NewValueId);
        Assert.Equal(PropertyType.Space, spaceChange.PropertyType);
        
        Assert.Null(epicChange.OldDisplayValue);
        Assert.Equal(epic.Name, epicChange.NewDisplayValue);
        Assert.Equal(epic.Id.ToString(), epicChange.NewValueId);
        Assert.Equal(PropertyType.Epic, epicChange.PropertyType);
        
        Assert.Null(statusChange.OldDisplayValue);
        Assert.Equal(status.Name, statusChange.NewDisplayValue);
        Assert.Equal(status.Id.ToString(), statusChange.NewValueId);
        Assert.Equal(PropertyType.Status, statusChange.PropertyType);
        
        Assert.Null(contentChange.OldDisplayValue);
        Assert.Equal("New Issue", contentChange.NewDisplayValue);
        Assert.Null(contentChange.PropertyName);
        Assert.Equal(PropertyType.Content, contentChange.PropertyType);
        
        Assert.Null(assigneeChange.OldDisplayValue);
        Assert.Equal("user1", assigneeChange.NewDisplayValue);
        Assert.Equal(userId.ToString(), assigneeChange.NewValueId);
        Assert.Equal(PropertyType.Assignee, assigneeChange.PropertyType);
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

        var historyChange = await testScope.Database.OrganizationLogs.Include(x => x.Items).SingleAsyncEF();
        Assert.Equal(issue.Id, historyChange.EntityId);
        Assert.Equal(LogEntityType.Issue, historyChange.EntityType);
        Assert.Equal(LogAction.Update, historyChange.Action);
        Assert.Equal(2, historyChange.Items!.Count);

        var contentChange = historyChange.Items[0];
        var assigneeChange = historyChange.Items[1];
        
        Assert.Equal("Hi", contentChange.OldDisplayValue);
        Assert.Equal("New", contentChange.NewDisplayValue);
        Assert.Null(contentChange.PropertyName);
        Assert.Null(contentChange.PropertyName);
        Assert.Equal(PropertyType.Content, contentChange.PropertyType);
        
        Assert.Equal("first_user", assigneeChange.OldDisplayValue);
        Assert.Equal("second_user", assigneeChange.NewDisplayValue);
        Assert.Null(assigneeChange.PropertyName);
        Assert.Equal(userId.ToString(), assigneeChange.OldValueId);
        Assert.Equal(participatorId.ToString(), assigneeChange.NewValueId);
        Assert.Null(assigneeChange.PropertyName);
        Assert.Equal(PropertyType.Assignee, assigneeChange.PropertyType);
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
        
        var historyChange = await testScope.Database.OrganizationLogs.Include(x => x.Items).SingleAsyncEF();
        Assert.Equal(issueData.Issue.Id, historyChange.EntityId);
        Assert.Equal(LogEntityType.Issue, historyChange.EntityType);
        Assert.Equal(LogAction.Delete, historyChange.Action);
        Assert.Empty(historyChange.Items!);
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
        Assert.Equal("AS", issueDto.Assignee.Initials);
        Assert.True(issueDto.Assignee.IsCurrentUser);
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
        Assert.False(issueDto.Assignee.IsCurrentUser);
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
    public async Task User_ShouldSearchIssuesByEpicStatus_WhenEpicStatusesProvided()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddIssueToDefaultStatus(userId, issue => issue.WithContent("Backlog Issue"))
                .AddSpace(userId, space => space
                    .AddEpic(userId, e => e
                        .WithStatus(EpicStatus.Done)
                        .AddIssue(userId, 0, issue => issue.WithContent("Done Epic Issue")))));

        var issuesResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    EpicStatuses = new[] { EpicStatus.Done },
                    Page = 0,
                    PerPage = 10,
                }));

        Assert.NotNull(issuesResult);
        var issueDto = Assert.Single(issuesResult.Data);
        Assert.Equal("Done Epic Issue", issueDto.Content);
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
                    .WithName("NEW SPACE")
                    .AddEpic(userId, e => e
                        .WithName("NEW EPIC")
                        .AddStatus(b => b
                            .WithName("NEW STATUS"))))
                .AddIssueToDefaultStatus(userId));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var defaultSpace = organization.GetSpace(0);
        var defaultEpic = organization.GetEpic(0, 0);
        var defaultStatus = organization.GetStatus(0, 0, 0);
        var newSpace = organization.GetSpace(1);
        var newEpic = organization.GetEpic(1, 1);
        var newStatus = organization.GetStatus(1, 1, 1);

        var request = new UpdateIssuesStatusRequest { IssueKeys = [issueData.Key], StatusId = newStatus.Id };
        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.UpdateStatus(request));

        var issue = await testScope.Database.Issues.FirstAsyncEF(x => x.Id == issueData.Issue.Id);
        Assert.Equal(newStatus.Id, issue.StatusId);
        
        var historyChange = await testScope.Database.OrganizationLogs.Include(x => x.Items).SingleAsyncEF();
        Assert.Equal(issue.Id, historyChange.EntityId);
        Assert.Equal(LogEntityType.Issue, historyChange.EntityType);
        Assert.Equal(LogAction.Update, historyChange.Action);
        Assert.Equal(3, historyChange.Items!.Count);
        
        var spaceChange = historyChange.Items[0];
        var epicChange = historyChange.Items[1];
        var statusChange = historyChange.Items[2];
        
        Assert.Equal(defaultSpace.Name, spaceChange.OldDisplayValue);
        Assert.Equal("NEW SPACE", spaceChange.NewDisplayValue);
        Assert.Equal(defaultSpace.Id.ToString(), spaceChange.OldValueId);
        Assert.Equal(newSpace.Id.ToString(), spaceChange.NewValueId);
        Assert.Equal(PropertyType.Space, spaceChange.PropertyType);
        
        Assert.Equal(defaultEpic.Name, epicChange.OldDisplayValue);
        Assert.Equal("NEW EPIC", epicChange.NewDisplayValue);
        Assert.Equal(defaultEpic.Id.ToString(), epicChange.OldValueId);
        Assert.Equal(newEpic.Id.ToString(), epicChange.NewValueId);
        Assert.Equal(PropertyType.Epic, epicChange.PropertyType);
        
        Assert.Equal(defaultStatus.Name, statusChange.OldDisplayValue);
        Assert.Equal("NEW STATUS", statusChange.NewDisplayValue);
        Assert.Equal(defaultStatus.Id.ToString(), statusChange.OldValueId);
        Assert.Equal(newStatus.Id.ToString(), statusChange.NewValueId);
        Assert.Equal(PropertyType.Status, statusChange.PropertyType);
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
        var keysMap = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.UpdateStatus(request));

        var issue = await testScope.Database.Issues
            .Where(x => x.Id == issueData.Issue.Id)
            .Select(x => new { x.StatusId, Key = new IssueKey(x.Status!.Epic!.Space!.Key, x.IssueNumber!.Number) })
            .FirstAsyncEF();
        
        Assert.Equal(newStatus.Id, issue.StatusId);
        
        var pair = Assert.Single(keysMap!);
        Assert.Equal(issueData.Key, pair.Key);
        Assert.Equal(issue.Key.ToString(), pair.Value);
        Assert.NotEqual(pair.Key, pair.Value);
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
        var userId = await testScope.CreateUser(x =>
        {
            x.TelegramUserName = "user1";
            x.Color = "#111111";
        });
        var participatorId = await testScope.CreateUser(x =>
        {
            x.TelegramUserName = "user2";
            x.Color = "#222222";
        });
        var organization = await testScope.InitializeOrganization(
            userId,
            initializer => initializer
                .AddListAttribute("Type", ["Bug", "Feature"])
                .AddListAttribute("Urgency", ["Low", "High"], "#333333")
                .AddTextAttribute("Note")
                .AddTextAttribute("Description", "#444444")
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

        var addCommentRequest = new AddCommentRequest
        {
            Text = "Comment 1",
            IssueKey = issueData.Key,
        };
        
        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.AddComment(addCommentRequest));

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

        Assert.Equal(2, historyData!.Data.Count);

        var commentChanged = historyData.Data[0];
        var issueChanged = historyData.Data[1];
        
        Assert.Equal(LogEntityType.Issue, issueChanged.EntityType);
        Assert.Equal(LogAction.Update, issueChanged.Action);
        Assert.Equal("user1", issueChanged.Owner.DisplayName);
        
        Assert.Equal(LogEntityType.Comment, commentChanged.EntityType);
        Assert.Equal(LogAction.Create, commentChanged.Action);
        Assert.Equal("user1", commentChanged.Owner.DisplayName);
        
        var issueChanges = issueChanged.Changes;
        Assert.Equal(6, issueChanges.Length);
        
        var contentChange = Assert.IsType<IssueHistoryContentChange>(issueChanges[0]);
        var assigneeChange = Assert.IsType<IssueHistoryAssigneeChange>(issueChanges[1]);
        var attachmentAddChange = Assert.IsType<IssueHistoryAttachmentChange>(issueChanges[2]);
        var attachmentDeleteChange = Assert.IsType<IssueHistoryAttachmentChange>(issueChanges[3]);
        var descriptionAttributeChange = Assert.IsType<IssueHistoryPropertyChange>(issueChanges[4]);
        var urgencyAttributeChange = Assert.IsType<IssueHistoryPropertyChange>(issueChanges[5]);
        
        Assert.Equal("Old", contentChange.OldContent);
        Assert.Equal("New", contentChange.NewContent);
        
        Assert.Equal("user2", assigneeChange.OldAssigneeDisplayName);
        Assert.Equal("user1", assigneeChange.NewAssigneeDisplayName);
        Assert.Equal("#222222", assigneeChange.OldAssigneeColor);
        Assert.Equal("#111111", assigneeChange.NewAssigneeColor);
        
        Assert.Equal("image.jpg", attachmentAddChange.FileName);
        Assert.NotNull(attachmentAddChange.PreviewFileId);
        Assert.Equal(AttachmentAction.Created, attachmentAddChange.Action);
        
        Assert.Equal("old.jpg", attachmentDeleteChange.FileName);
        Assert.Equal(issueData.Issue.IssueAttachments[0].Attachment!.PreviewFileId, attachmentDeleteChange.PreviewFileId);
        Assert.Equal(AttachmentAction.Deleted, attachmentDeleteChange.Action);
        
        Assert.Equal("Description", descriptionAttributeChange.PropertyName);
        Assert.Equal(AttributeType.Text, descriptionAttributeChange.AttributeType);
        Assert.Null(descriptionAttributeChange.NewValueName);
        Assert.Equal("#444444", descriptionAttributeChange.NewValueColor);
        Assert.Equal("50 cents debt", descriptionAttributeChange.OldValueName);
        Assert.Equal("#444444", descriptionAttributeChange.OldValueColor);
        
        Assert.Equal("Urgency", urgencyAttributeChange.PropertyName);
        Assert.Equal(AttributeType.List, urgencyAttributeChange.AttributeType);
        Assert.Equal("High", urgencyAttributeChange.NewValueName);
        Assert.Equal("Low", urgencyAttributeChange.OldValueName);
        Assert.Equal("#333333", urgencyAttributeChange.NewValueColor);
        Assert.Equal("#333333", urgencyAttributeChange.OldValueColor);

        var commentChange = Assert.IsType<IssueHistoryContentChange>(Assert.Single(commentChanged.Changes));
        Assert.Null(commentChange.OldContent);
        Assert.Equal("Comment 1", commentChange.NewContent);
    }

    [Fact]
    public async Task Update_ShouldClearAllAttributeValues_WhenAttributeValuesIsEmpty()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            initializer => initializer
                .AddTextAttribute("Note")
                .AddIssueToDefaultStatus(userId, builder => builder
                    .WithAttributeValue(0, "Ask mr. John")
                    .WithContent("Old")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);

        var updateIssueRequest = new UpdateIssueRequest
        {
            AssigneeId = userId,
            Content = "Old",
            AttributeValues = [],
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

        var issueChanged = Assert.Single(historyData!.Data);
        var noteAttributeChange = Assert.IsType<IssueHistoryPropertyChange>(Assert.Single(issueChanged.Changes));

        Assert.Equal("Note", noteAttributeChange.PropertyName);
        Assert.Equal("Ask mr. John", noteAttributeChange.OldValueName);
        Assert.Null(noteAttributeChange.NewValueName);
    }

    [Fact]
    public async Task Update_ShouldLogDecimalAndDateTimeAttributeChanges_WhenAttributesChanged()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var oldDueDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newDueDate = new DateTime(2026, 2, 2, 12, 30, 0, DateTimeKind.Utc);
        var organization = await testScope.InitializeOrganization(
            userId,
            initializer => initializer
                .AddDecimalAttribute("Points")
                .AddDateTimeAttribute("Due")
                .AddIssueToDefaultStatus(userId, builder => builder
                    .WithAttributeValue(0, 5m)
                    .WithAttributeValue(1, oldDueDate)
                    .WithContent("Old")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);
        var pointsAttribute = organization.Attributes![0];
        var dueAttribute = organization.Attributes![1];

        var updateIssueRequest = new UpdateIssueRequest
        {
            AssigneeId = userId,
            Content = "Old",
            AttributeValues =
            [
                new DecimalAttributeValue
                {
                    AttributeId = pointsAttribute.Id,
                    Value = 8m,
                },
                new DateTimeAttributeValue
                {
                    AttributeId = dueAttribute.Id,
                    Value = newDueDate,
                },
            ],
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

        var issueChanged = Assert.Single(historyData!.Data);
        Assert.Equal(2, issueChanged.Changes.Length);

        var pointsChange = Assert.IsType<IssueHistoryPropertyChange>(issueChanged.Changes[0]);
        var dueChange = Assert.IsType<IssueHistoryPropertyChange>(issueChanged.Changes[1]);

        Assert.Equal("Points", pointsChange.PropertyName);
        Assert.Equal("5", pointsChange.OldValueName);
        Assert.Equal("8", pointsChange.NewValueName);

        Assert.Equal("Due", dueChange.PropertyName);
        Assert.Equal(oldDueDate.ToString("O"), dueChange.OldValueName);
        Assert.Equal(newDueDate.ToString("O"), dueChange.NewValueName);

        var issueDto = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetIssue(issueData.Key));

        var pointsValue = Assert.Single(issueDto!.AttributeValues, x => x.Id == pointsAttribute.Id);
        var dueValue = Assert.Single(issueDto.AttributeValues, x => x.Id == dueAttribute.Id);

        Assert.Equal("8", pointsValue.Value);
        Assert.Equal(newDueDate.ToString("O"), dueValue.Value);
    }

    [Fact]
    public async Task Search_ShouldFilterIssuesByDecimalAttributeRange_WhenMinAndMaxProvided()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            initializer => initializer
                .AddDecimalAttribute("Points")
                .AddIssueToDefaultStatus(userId, i => i.WithContent("Low").WithAttributeValue(0, 1m))
                .AddIssueToDefaultStatus(userId, i => i.WithContent("Mid").WithAttributeValue(0, 5m))
                .AddIssueToDefaultStatus(userId, i => i.WithContent("High").WithAttributeValue(0, 10m)));

        var pointsAttribute = organization.Attributes![0];

        var filters = new Dictionary<long, AttributeFilterValue>
        {
            [pointsAttribute.Id] = new DecimalAttributeFilterValue { Min = 3m, Max = 8m },
        };

        var issuesResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    Page = 0,
                    PerPage = 10,
                    Filters = filters,
                }));

        var issueDto = Assert.Single(issuesResult!.Data);
        Assert.Equal("Mid", issueDto.Content);
    }

    [Fact]
    public async Task Search_ShouldFilterIssuesByDateAttributeRange_WhenFromAndToProvided()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            initializer => initializer
                .AddDateAttribute("Due")
                .AddIssueToDefaultStatus(userId, i => i.WithContent("Early").WithAttributeValue(0, new DateOnly(2026, 1, 1)))
                .AddIssueToDefaultStatus(userId, i => i.WithContent("Middle").WithAttributeValue(0, new DateOnly(2026, 3, 1)))
                .AddIssueToDefaultStatus(userId, i => i.WithContent("Late").WithAttributeValue(0, new DateOnly(2026, 6, 1))));

        var dueAttribute = organization.Attributes![0];

        var filters = new Dictionary<long, AttributeFilterValue>
        {
            [dueAttribute.Id] = new DateAttributeFilterValue
            {
                From = new DateOnly(2026, 2, 1),
                To = new DateOnly(2026, 4, 1),
            },
        };

        var issuesResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    Page = 0,
                    PerPage = 10,
                    Filters = filters,
                }));

        var issueDto = Assert.Single(issuesResult!.Data);
        Assert.Equal("Middle", issueDto.Content);
    }

    [Fact]
    public async Task Search_ShouldFilterIssuesByDateTimeAttributeRange_WhenFromAndToProvided()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            initializer => initializer
                .AddDateTimeAttribute("Due")
                .AddIssueToDefaultStatus(userId, i => i.WithContent("Early").WithAttributeValue(0, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)))
                .AddIssueToDefaultStatus(userId, i => i.WithContent("Middle").WithAttributeValue(0, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)))
                .AddIssueToDefaultStatus(userId, i => i.WithContent("Late").WithAttributeValue(0, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc))));

        var dueAttribute = organization.Attributes![0];

        var filters = new Dictionary<long, AttributeFilterValue>
        {
            [dueAttribute.Id] = new DateTimeAttributeFilterValue
            {
                From = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                To = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            },
        };

        var issuesResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    Page = 0,
                    PerPage = 10,
                    Filters = filters,
                }));

        var issueDto = Assert.Single(issuesResult!.Data);
        Assert.Equal("Middle", issueDto.Content);
    }

    [Fact]
    public async Task Search_ShouldFilterIssuesByIntegerAttributeRange_WhenMinAndMaxProvided()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            initializer => initializer
                .AddIntegerAttribute("Rank")
                .AddIssueToDefaultStatus(userId, i => i.WithContent("Low").WithAttributeValue(0, 1L))
                .AddIssueToDefaultStatus(userId, i => i.WithContent("Mid").WithAttributeValue(0, 5L))
                .AddIssueToDefaultStatus(userId, i => i.WithContent("High").WithAttributeValue(0, 10L)));

        var rankAttribute = organization.Attributes![0];

        var filters = new Dictionary<long, AttributeFilterValue>
        {
            [rankAttribute.Id] = new IntegerAttributeFilterValue { Min = 3, Max = 8 },
        };

        var issuesResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    Page = 0,
                    PerPage = 10,
                    Filters = filters,
                }));

        var issueDto = Assert.Single(issuesResult!.Data);
        Assert.Equal("Mid", issueDto.Content);
    }

    [Fact]
    public async Task Search_ShouldSortIssuesByScalarAttributes_WhenSortingProvided()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            initializer => initializer
                .AddDecimalAttribute("Points")
                .AddDateTimeAttribute("Due")
                .AddIssueToDefaultStatus(userId, i => i
                    .WithContent("C")
                    .WithAttributeValue(0, 3m)
                    .WithAttributeValue(1, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)))
                .AddIssueToDefaultStatus(userId, i => i
                    .WithContent("A")
                    .WithAttributeValue(0, 1m)
                    .WithAttributeValue(1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)))
                .AddIssueToDefaultStatus(userId, i => i
                    .WithContent("B")
                    .WithAttributeValue(0, 2m)
                    .WithAttributeValue(1, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc))));

        var pointsAttribute = organization.Attributes![0];
        var dueAttribute = organization.Attributes![1];

        var byNumber = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    Page = 0,
                    PerPage = 10,
                    Sorting = new ByAttributeIssueSorting
                    {
                        AttributeId = pointsAttribute.Id,
                        Direction = SortingDirection.Ascending,
                    },
                }));

        Assert.Equal(["A", "B", "C"], byNumber!.Data.Select(x => x.Content));

        var byDate = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    Page = 0,
                    PerPage = 10,
                    Sorting = new ByAttributeIssueSorting
                    {
                        AttributeId = dueAttribute.Id,
                        Direction = SortingDirection.Descending,
                    },
                }));

        Assert.Equal(["C", "B", "A"], byDate!.Data.Select(x => x.Content));
    }

    [Fact]
    public async Task Summarize_ShouldReturnBeautifiedContent_WhenAiSummarizerSucceeds()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        const string beautified = "Fix login bug\n---\n- Login fails on retry\n- Add logging";

        host.AiContentSummarizerMock
            .Setup(x => x.SummarizeAsync(
                "fix login bug, fails on retry, need logs pls",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(beautified);

        var result = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Summarize(
                new SummarizeIssueContentRequest
                {
                    Content = "fix login bug, fails on retry, need logs pls",
                }));

        Assert.Equal(beautified, result);
    }

    [Fact]
    public async Task Summarize_ShouldReturn503_WhenAiSummarizerFails()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        host.AiContentSummarizerMock
            .Setup(x => x.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiContentSummarizationException("DeepSeek API request failed."));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Summarize(
                new SummarizeIssueContentRequest
                {
                    Content = "notes",
                })));

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }
}
