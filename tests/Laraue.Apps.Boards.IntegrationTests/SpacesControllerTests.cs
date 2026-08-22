using System.Net;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class SpacesControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<SpacesController> _spacesController = host.Controller<SpacesController>();
    
    [Fact]
    public async Task User_ShouldCreateSpaceInOwnedOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        
        var spaceKey = await _spacesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(
                new CreateSpaceRequest
                {
                    Name = "Space 1",
                    Color = "#ffffff",
                    Key = "SPA"
                }));

        var spaces = await testScope.Database.Spaces.Include(x => x.Epics).ToListAsyncEF();
        
        var space = spaces.First(x => x.Key == spaceKey);
        Assert.Equal("Space 1", space.Name);
        Assert.Equal("#ffffff", space.Color);
        Assert.Equal(userId, space.CreatorId);
        Assert.True(space.CreatedAt != default);
        Assert.True(space.UpdatedAt != default);
        Assert.False(space.IsDefault);
        
        var epic = Assert.Single(space.Epics!);
        Assert.True(epic.IsDefault);
    }
    
    [Fact]
    public async Task User_ShouldCreateSpaceInOrganization_WhenHasAccess()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(participatorId, builder => builder
                .SetGlobalAccessLevel(x => x.CanCreateSpaces = true)));
        
        var spaceKey = await _spacesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Create(
                new CreateSpaceRequest
                {
                    Name = "Space 1",
                    Color = "#ffffff",
                    Key = "SPA",
                }));

        var spaces = await testScope.Database.Spaces.Include(s => s.Users).ToListAsyncEF();
        var space = spaces.First(x => x.Key == spaceKey);
        Assert.Equal(participatorId, space.CreatorId);
    }
    
    [Fact]
    public async Task User_ShouldNotCreateSpaceInOrganization_WhenHasNotAccess()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(participatorId, builder => builder
                .SetGlobalAccessLevel(x => x.CanRead = true)));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _spacesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.Create(
                new CreateSpaceRequest
                {
                    Name = "Space 1",
                    Color = "#ffffff",
                    Key = "SPA"
                })));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }
    
    [Fact]
    public async Task User_ShouldViewSpacesInOwnedOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(participatorId)
            .AddSpace(participatorId, s => s
                .WithName("Space created by Participator")));

        var spaceKey = organization.Spaces![1].Key;
        
        var spaces = await _spacesController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetAll());
        
        Assert.Equal(2, spaces!.Length);
        var space = spaces.First(x => x.Key == spaceKey);
        Assert.Equal("Space created by Participator", space.Name);
    }
    
    [Fact]
    public async Task User_ShouldViewSpacesInOrganization_WhenHasAccessOnOrganizationLevel()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId)
            .AddUser(participatorId, builder => builder
                .SetGlobalAccessLevel(x => x.CanRead = true)));
        
        var spaces = await _spacesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.GetAll());
        
        Assert.Equal(2, spaces!.Length);
    }
    
    [Fact]
    public async Task User_ShouldViewSpacesInOrganization_WhenHasAccessOnSpacesLevel()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId)
            .AddUser(participatorId, builder => builder
                .SetGlobalAccessLevel(x => x.CanRead = true)));
        
        var spaces = await _spacesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.GetAll());
        
        Assert.Equal(2, spaces!.Length);
    }
    
    [Fact]
    public async Task User_ShouldNotViewSpacesInOrganization_WhenHasNotAccess()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId)
            .AddUser(participatorId, builder => builder
                .SetGlobalAccessLevel(x => x.CanRead = true)));
        
        var spaces = await _spacesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.GetAll());
        
        Assert.Equal(2, spaces!.Length);
    }
    
    [Fact]
    public async Task User_ShouldViewEpicsInOwnedOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(participatorId)
            .AddSpace(participatorId, s => s
                .WithName("Space created by Participator")));

        var spaceKey = organization.Spaces![1].Key;
        
        var epics = await _spacesController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetSpaceEpics(spaceKey));
        
        var epic = Assert.Single(epics!);
        Assert.Equal("Backlog", epic.Name);
    }
    
    [Fact]
    public async Task User_ShouldViewEpics_WhenHasSpaceLevelPermission()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(participatorId, b => b
                .SetSpaceAccessLevel(1, x => x.CanRead = true))
            .AddSpace(ownerId, s => s
                .AddEpic(ownerId)));

        var spaceKey = organization.Spaces![1].Key;
        
        var epics = await _spacesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.GetSpaceEpics(spaceKey));
        
        Assert.Equal(2, epics!.Length);
    }
    
    [Fact]
    public async Task User_ShouldNotViewEpics_WhenHasAnotherSpaceLevelPermission()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(participatorId, b => b
                .SetSpaceAccessLevel(0, x => x.CanRead = true))
            .AddSpace(ownerId, s => s
                .AddEpic(ownerId)));

        var spaceKey = organization.Spaces![1].Key;
        
        var epics = await _spacesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.GetSpaceEpics(spaceKey));
        
        Assert.Empty(epics!);
    }
    
    [Fact]
    public async Task User_ShouldViewEpics_WhenHasEpicsLevelPermission()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(participatorId, b => b
                .SetGlobalAccessLevel(x => x.CanRead = true))
            .AddSpace(ownerId, s => s
                .AddEpic(ownerId)));

        var spaceKey = organization.Spaces![1].Key;
        
        var epics = await _spacesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.GetSpaceEpics(spaceKey));
        
        Assert.Equal(2, epics!.Length);
    }
    
    [Fact]
    public async Task User_ShouldViewEpics_WhenHasSpaceEpicsLevelPermission()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(participatorId, b => b
                .SetSpaceAccessLevel(1, x => x.CanRead = true))
            .AddSpace(ownerId, s => s
                .AddEpic(ownerId)));

        var spaceKey = organization.Spaces![1].Key;
        
        var epics = await _spacesController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.GetSpaceEpics(spaceKey));

        Assert.Equal(2, epics!.Length);
    }

    [Fact]
    public async Task User_ShouldReturnAllEpics_WhenStatusesFilterIsNull()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, s => s
                .AddEpic(ownerId, e => e.WithName("In Progress Epic").WithStatus(EpicStatus.Active))
                .AddEpic(ownerId, e => e.WithName("Done Epic").WithStatus(EpicStatus.Done))));

        var spaceKey = organization.Spaces![1].Key;

        var epics = await _spacesController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetSpaceEpics(spaceKey));

        Assert.Equal(3, epics!.Length);
    }

    [Fact]
    public async Task User_ShouldFilterEpicsByStatus_WhenSingleStatusProvided()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, s => s
                .AddEpic(ownerId, e => e.WithName("In Progress Epic").WithStatus(EpicStatus.Active))
                .AddEpic(ownerId, e => e.WithName("Done Epic").WithStatus(EpicStatus.Done))));

        var spaceKey = organization.Spaces![1].Key;

        var epics = await _spacesController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetSpaceEpics(spaceKey, new[] { EpicStatus.Done }));

        var epic = Assert.Single(epics!);
        Assert.Equal("Done Epic", epic.Name);
        Assert.Equal(EpicStatus.Done, epic.Status);
    }

    [Fact]
    public async Task User_ShouldFilterEpicsByStatus_WhenMultipleStatusesProvided()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, s => s
                .AddEpic(ownerId, e => e.WithName("In Progress Epic").WithStatus(EpicStatus.Active))
                .AddEpic(ownerId, e => e.WithName("Done Epic").WithStatus(EpicStatus.Done))));

        var spaceKey = organization.Spaces![1].Key;

        var epics = await _spacesController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            // The default "Backlog" epic is created with EpicStatus.New.
            .Execute(x => x.GetSpaceEpics(spaceKey, new[] { EpicStatus.New, EpicStatus.Done }));

        Assert.Equal(2, epics!.Length);
        Assert.Contains(epics!, e => e.Name == "Backlog");
        Assert.Contains(epics!, e => e.Name == "Done Epic");
    }

    [Fact]
    public async Task User_ShouldViewSpaceMembers_Always()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser(x =>
        {
            x.TelegramUserName = "aa";
            x.Color = "#111111";
        });
        var spaceMemberId = await testScope.CreateUser(x =>
        {
            x.TelegramUserName = "bb";
            x.Color = "#222222";
        });
        var otherSpaceMemberId = await testScope.CreateUser(x => x.TelegramUserName = "cc");
        
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(spaceMemberId, b => b
                .SetSpaceAccessLevel(0, x => x.CanRead = true)) // User in the requested space
            .AddSpace(ownerId)
            .AddUser(otherSpaceMemberId, b => b
                .SetSpaceAccessLevel(1, x => x.CanRead = true))); // User in different space

        var spaceKey = organization.Spaces![0].Key;
        
        var members = await _spacesController
            .WithOrganizationAuthorization(organization.Id, otherSpaceMemberId)
            .Execute(x => x.GetSpaceMembers(spaceKey));
        
        Assert.Equal(2, members!.Length);
        Assert.Equal(["AA", "BB"], members.Select(x => x.Initials).OrderBy(x => x));
        Assert.Equal(["#111111", "#222222"], members.Select(x => x.Color).OrderBy(x => x));
        Assert.DoesNotContain(members, x => x.IsCurrentUser);

        members = await _spacesController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetSpaceMembers(spaceKey));

        Assert.Equal(ownerId, Assert.Single(members!, x => x.IsCurrentUser).UserId);
    }
}
