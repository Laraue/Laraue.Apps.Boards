using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using LinqToDB.Async;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class EpicControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<EpicsController> _epicsController = host.Controller<EpicsController>();
    
    [Fact]
    public async Task User_ShouldCreateEpicInOwnedOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, org => org
            .AddSpace(userId));
        
        var spaceKey = organization.Spaces![1].Key;
        var epicId = await _epicsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(
                new CreateEpicRequest
                {
                    Name = "Epic 1",
                    Color = "#fffff1",
                    SpaceKey = spaceKey,
                }));

        var epics = await testScope.Database.Epics
            .Include(e => e.Statuses)
            .ToListAsyncEF();
        
        var epic = epics.First(x => x.Id == epicId);
        Assert.Equal("Epic 1", epic.Name);
        Assert.Equal("#fffff1", epic.Color);
        Assert.Equal(userId, epic.UserId);
        Assert.True(epic.CreatedAt != default);
        Assert.True(epic.UpdatedAt != default);
        Assert.True(epic.TouchedAt != default);
        Assert.False(epic.IsDefault);
        
        var status = Assert.Single(epic.Statuses!);
        Assert.Equal("New", status.Name);
    }
    
    [Fact]
    public async Task User_ShouldCreateEpicInOrganization_WhenHasAccessOnOrganizationLevel()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(participatorId, builder => builder
                .SetGlobalAccessLevel(x => x.CanCreateEpics = true)));
        
        var spaceKey = organization.Spaces![0].Key;
        
        var epicId = await _epicsController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Create(
                new CreateEpicRequest
                {
                    Name = "Epic 1",
                    Color = "#fffff1",
                    SpaceKey = spaceKey,
                }));
        
        Assert.NotEqual(0, epicId);
    }
    
    [Fact]
    public async Task User_ShouldCreateEpicInOrganization_WhenHasAccessOnSpaceLevel()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(participatorId, builder => builder
                .SetSpaceAccessLevel(0, x => x.CanCreateEpics = true)));
        
        var spaceKey = organization.Spaces![0].Key;
        
        var epicId = await _epicsController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Create(
                new CreateEpicRequest
                {
                    Name = "Epic 1",
                    Color = "#fffff1",
                    SpaceKey = spaceKey,
                }));
        
        Assert.NotEqual(0, epicId);
    }
    
    [Fact]
    public async Task User_ShouldGetEpic_WhenHasAccess()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId,
            setup => setup
                .AddSpace(userId)
                .AddUser(participatorId, u => u
                    .SetGlobalAccessLevel(x => x.CanRead = true)));

        var epicId = organization.GetEpic(1, 0).Id;
        
        var epic = await _epicsController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Get(epicId));
        
        Assert.NotNull(epic);
        Assert.False(epic.CanDelete);
        Assert.False(epic.CanUpdate);
        Assert.False(epic.CanCreateIssues);
        Assert.False(epic.CanDeleteIssues);
        Assert.False(epic.CanUpdateIssues);
    }
    
    [Fact]
    public async Task User_ShouldUpdateEpicInOrganization_WhenHasDirectEditAccess()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(participatorId, builder => builder
                .SetSpaceAccessLevel(0,  x => x.CanUpdateEpics = true)));
        
        var epicId = organization.GetEpic(0, 0).Id;
        
        await _epicsController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Update(
                epicId,
                new UpdateEpicRequest
                {
                    Name = "Epic 1",
                    Color = "#fffff1",
                }));
        
        var epic = await testScope.Database.Epics.FirstAsyncEF(x => x.Id == epicId);
        Assert.Equal("Epic 1", epic.Name);
    }
    
    [Fact]
    public async Task User_ShouldDeleteNotDefaultEpicInOrganization_WhenHasDirectDeleteAccess()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(participatorId, space => space.AddEpic(participatorId))
            .AddUser(participatorId, builder => builder
                .SetSpaceAccessLevel(1,  x => x.CanDeleteEpics = true)));
        
        var epicId = organization.GetEpic(1, 1).Id;
        
        await _epicsController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Delete(epicId));
        
        var epic = await testScope.Database.Epics.FirstOrDefaultAsyncEF(x => x.Id == epicId);
        Assert.Null(epic);
    }
}
