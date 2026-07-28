using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class IssuesControllerTests(WebApiTestHost host)  : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<IssuesController> _issuesController = host.Controller<IssuesController>();
    
    [Fact]
    public async Task User_ShouldCreateIssue_WhenIsOrganizationOwner()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
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
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
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
                    AssigneeId = userId,
                }));

        var issue = await testScope.Database.FindIssueByKey(organization.Id, issueData.Key);
        Assert.NotNull(issue);
        Assert.Equal("New", issue.Content);
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
        Assert.Equal(space.Id, issueDto.SpaceId);
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
}
