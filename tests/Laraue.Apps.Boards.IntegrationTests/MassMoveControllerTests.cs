using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class MassMoveControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<MovementController> _controller = host.Controller<MovementController>();
    
    [Fact]
    public async Task User_ShouldMoveNotDefaultSpace_WhenHasMassMovePermission()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var personalOrganization = await testScope.InitializePersonalOrganization(
            userId,
            o => o.AddSpace(userId));
        var organization = await testScope.InitializeOrganization(userId);

        var spaceToMove = personalOrganization.GetSpace(1);
        
        await _controller
            .WithOrganizationAuthorization(personalOrganization.Id, userId)
            .Execute(x => x.MoveSpace(spaceToMove.Key, organization.Id));

        var resultSpace = await testScope.Database.Spaces.FirstAsyncEF(e => e.Id == spaceToMove.Id);
        Assert.Equal(organization.Id, resultSpace.OrganizationId);
    }
    
    [Fact]
    public async Task User_ShouldNotMoveDefaultSpace_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var personalOrganization = await testScope.InitializePersonalOrganization(
            userId,
            o => o.AddSpace(userId));
        var organization = await testScope.InitializeOrganization(userId);

        var spaceToMove = personalOrganization.GetSpace(0);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _controller
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.MoveSpace(spaceToMove.Key, organization.Id)));

        var forbidden = ex.HasInnerException<ForbiddenException>();
        Assert.Equal("Default space cannot be moved.", forbidden.Message);
    }
    
    [Fact]
    public async Task User_ShouldMoveSpaceEpics_WhenHasMassMovePermission()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var personalOrganization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddSpace(userId, s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, builder => builder
                            .WithContent("Moving issue")
                            .AddComment(userId, "Just a comment")))));
        
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddIssueToDefaultStatus(userId, builder => builder
                    .WithContent("Old issue")));

        var sourceSpace = personalOrganization.GetSpace(1);
        var backlogEpic = personalOrganization.GetEpic(1, 0); // Backlog should not be moved
        var epicToMove = personalOrganization.GetEpic(1, 1); // Other epics should be moved
        var issueData = personalOrganization.GetIssueData(1, 1, 0, 0);
        var commentId = issueData.Issue.IssueComments![0].Id;
        
        var spaceToReceive = organization.GetSpace(0);
        
        var issueNumbers = await testScope.Database.IssueNumbers
            .OrderBy(i => i.IssueId)
            .Select(x => new { x.Number, x.Issue!.Content, x.SpaceId })
            .ToListAsyncEF();

        // Assert issue numbers before moving
        var firstIssue = issueNumbers[0];
        Assert.Equal(sourceSpace.Id, firstIssue.SpaceId);
        Assert.Equal(1, firstIssue.Number);
        Assert.Equal("Moving issue", firstIssue.Content);
        
        var secondIssue = issueNumbers[1];
        Assert.Equal(spaceToReceive.Id, secondIssue.SpaceId);
        Assert.Equal(1, secondIssue.Number);
        Assert.Equal("Old issue", secondIssue.Content);
        
        // Insert issue log entries. It should be moved to new org
        testScope.Database.OrganizationLogs.Add(new OrganizationLog
        {
            EntityId = issueData.Issue.Id,
            OrganizationId = personalOrganization.Id,
            OwnerId = userId,
            EntityType = LogEntityType.Issue,
        });
        
        // Comment related to the issue. So it should be also moved
        testScope.Database.OrganizationLogs.Add(new OrganizationLog
        {
            EntityId = commentId,
            OrganizationId = personalOrganization.Id,
            OwnerId = userId,
            EntityType = LogEntityType.Comment,
        });
        await testScope.Database.SaveChangesAsync();

        var request = new MoveSpaceEpicsRequest
        {
            NewSpaceKey = spaceToReceive.Key,
            NewOrganizationId = spaceToReceive.OrganizationId,
            SourceSpaceKey = sourceSpace.Key,
        };
        
        await _controller
            .WithOrganizationAuthorization(personalOrganization.Id, userId)
            .Execute(x => x.MoveSpaceEpics(request));
        
        var movedEpic = await testScope.Database.Epics.FirstAsyncEF(e => e.Id == epicToMove.Id);
        Assert.Equal(spaceToReceive.Id, movedEpic.SpaceId);
        
        // Assert issue numbers after moving
        issueNumbers = await testScope.Database.IssueNumbers
            .OrderBy(i => i.IssueId)
            .Select(x => new { x.Number, x.Issue!.Content, x.SpaceId })
            .ToListAsyncEF();

        firstIssue = issueNumbers[0];
        Assert.Equal(spaceToReceive.Id, firstIssue.SpaceId);
        Assert.Equal(2, firstIssue.Number);
        Assert.Equal("Moving issue", firstIssue.Content);
        
        secondIssue = issueNumbers[1];
        Assert.Equal(spaceToReceive.Id, secondIssue.SpaceId);
        Assert.Equal(1, secondIssue.Number);
        Assert.Equal("Old issue", secondIssue.Content);

        var counters = await testScope.Database.SpaceCounters
            .ToDictionaryAsyncEF(x => x.SpaceId, x => x.LastNumber);
        
        Assert.Equal(1, counters[sourceSpace.Id]);
        Assert.Equal(2, counters[spaceToReceive.Id]);
        
        var notMovedEpic = await testScope.Database.Epics.FirstAsyncEF(e => e.Id == backlogEpic.Id);
        Assert.Equal(sourceSpace.Id, notMovedEpic.SpaceId);

        var logs = await testScope.Database.OrganizationLogs.OrderBy(x => x.Id).ToListAsyncEF();
        Assert.Equal(2, logs.Count);

        Assert.All(logs, log =>
        {
            Assert.Equal(organization.Id, log.OrganizationId);
        });
    }
    
    [Fact]
    public async Task User_ShouldMoveNotDefaultEpic_WhenHasMassMovePermission()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var personalOrganization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddSpace(userId, s => s
                    .AddEpic(userId)));
        var organization = await testScope.InitializeOrganization(userId);
        var epicToMove = personalOrganization.GetEpic(1, 1);
        var spaceToReceive = organization.GetSpace(0);
        
        var moveRequest = new MoveEpicRequest
        {
            NewSpaceKey = spaceToReceive.Key,
            NewOrganizationId = spaceToReceive.OrganizationId,
            SourceEpicId = epicToMove.Id
        };
        
        await _controller
            .WithOrganizationAuthorization(personalOrganization.Id, userId)
            .Execute(x => x.MoveEpic(moveRequest));
        
        var movedEpic = await testScope.Database.Epics.FirstAsyncEF(e => e.Id == epicToMove.Id);
        Assert.Equal(spaceToReceive.Id, movedEpic.SpaceId);
    }
    
    [Fact]
    public async Task User_ShouldNotMoveDefaultEpic_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var personalOrganization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddSpace(userId, s => s
                    .AddEpic(userId)));
        var organization = await testScope.InitializeOrganization(userId);
        var epicToMove = personalOrganization.GetEpic(1, 0);
        var spaceToReceive = organization.GetSpace(0);
        
        var moveRequest = new MoveEpicRequest
        {
            NewSpaceKey = spaceToReceive.Key,
            NewOrganizationId = spaceToReceive.OrganizationId,
            SourceEpicId = epicToMove.Id
        };
        
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _controller
            .WithOrganizationAuthorization(personalOrganization.Id, userId)
            .Execute(x => x.MoveEpic(moveRequest)));
        
        var forbidden = ex.HasInnerException<ForbiddenException>();
        Assert.Equal("Default epic cannot be moved.", forbidden.Message);
    }
    
    [Fact]
    public async Task User_ShouldMoveEpic_WhenHasMassMovePermission()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var userCanNotMoveDueToMassMoveInSourceMissing = await testScope.CreateUser();
        var userCanNotMoveDueToEpicCreationInTargetMissing = await testScope.CreateUser();
        var userCanMove = await testScope.CreateUser();
        
        var sourceOrganization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(userCanNotMoveDueToMassMoveInSourceMissing)
                .AddUser(userCanNotMoveDueToEpicCreationInTargetMissing, permissions => permissions
                    .SetAdminAccessLevel(AdminAccessLevel.MassMove))
                .AddUser(userCanMove, permissions => permissions
                    .SetAdminAccessLevel(AdminAccessLevel.MassMove))
                .AddSpace(userId, s => s
                    .AddEpic(userId)));
        
        var destinationOrganization = await testScope.InitializeOrganization(
            userId,
            o =>
                o
                    .AddUser(userCanNotMoveDueToEpicCreationInTargetMissing)
                    .AddUser(userCanMove, permissions => permissions
                    .SetGlobalAccessLevel(x => x.CanCreateEpics = true)));
        
        var epicToMove = sourceOrganization.GetEpic(1, 1);
        var spaceToReceive = destinationOrganization.GetSpace(0);

        var moveRequest = new MoveEpicRequest
        {
            NewSpaceKey = spaceToReceive.Key,
            NewOrganizationId = spaceToReceive.OrganizationId,
            SourceEpicId = epicToMove.Id
        };
        
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _controller
            .WithOrganizationAuthorization(sourceOrganization.Id, userCanNotMoveDueToMassMoveInSourceMissing)
            .Execute(x => x.MoveEpic(moveRequest)));
        
        var notFound = ex.HasInnerException<NotFoundException>();
        Assert.Equal($"Organization: {sourceOrganization.Id} is unavailable or permission: MassMove is missing", notFound.Message);
        
        ex = await Assert.ThrowsAsync<HttpRequestException>(() => _controller
            .WithOrganizationAuthorization(sourceOrganization.Id, userCanNotMoveDueToEpicCreationInTargetMissing)
            .Execute(x => x.MoveEpic(moveRequest)));
        
        var forbidden = ex.HasInnerException<ForbiddenException>();
        Assert.Equal($"Space is not exists: {spaceToReceive.Key} or epic creation is forbidden", forbidden.Message);
        
        await _controller
            .WithOrganizationAuthorization(sourceOrganization.Id, userCanMove)
            .Execute(x => x.MoveEpic(moveRequest));
        
        var movedEpic = await testScope.Database.Epics.FirstAsyncEF(e => e.Id == epicToMove.Id);
        Assert.Equal(spaceToReceive.Id, movedEpic.SpaceId);
    }

    [Fact]
    public async Task GetDestinationSpaces_ShouldReturnOnlySpaces_WhereUserCanCreateEpics()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        
        var sourceOrganization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId, u => u
                    .SetAdminAccessLevel(AdminAccessLevel.MassMove)));
        
        var destinationOrganization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddSpace(userId, "SP1")
                .AddSpace(userId, "SP2", s => s.WithName("Allowed"))
                .AddUser(participatorId, u => u // User has created epics access only to last space
                    .SetSpaceAccessLevel(2, x => x.CanCreateEpics = true)));
        
        var allowedSpaces = await _controller
            .WithOrganizationAuthorization(sourceOrganization.Id, participatorId)
            .Execute(x => x.GetDestinationSpaces(destinationOrganization.Id));
        
        Assert.NotNull(allowedSpaces);
        var allowedSpace = Assert.Single(allowedSpaces);
        Assert.Equal("Allowed", allowedSpace.Name);
    }
}