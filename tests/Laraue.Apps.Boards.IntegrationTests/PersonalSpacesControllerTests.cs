using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class PersonalSpacesControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<SpacesController> _spacesController = host.Controller<SpacesController>();

    [Fact]
    public async Task User_ShouldCreatePersonalSpace_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(userId);
        
        var spaceKey = await _spacesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(
                new CreateSpaceRequest
                {
                    Name = "Space 1",
                    Color = "#ffffff",
                    Key = "spa"
                }));

        var spaces = await testScope.Database.Spaces.ToListAsyncEF();
        
        var space = spaces.First(x => x.Key == spaceKey);
        Assert.Equal("Space 1", space.Name);
        Assert.Equal("#ffffff", space.Color);
        Assert.Equal("SPA", space.Key);
        Assert.Equal(userId, space.CreatorId);
        Assert.True(space.CreatedAt != default);
        Assert.True(space.UpdatedAt != default);
    }
    
    [Fact]
    public async Task User_ShouldUpdatePersonalSpace_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var timestamp = DateTime.UtcNow;
        
        var organization = await testScope.InitializePersonalOrganization(userId, org => org
            .AddSpace(userId, space => space.WithTimestamp(timestamp)));

        var spaceId = organization.Spaces![1].Id;
        
        await _spacesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Update(
                spaceId,
                new UpdateSpaceRequest
                {
                    Name = "Space 1",
                    Color = "#ffffff",
                    Key = "spb"
                }));

        var spaces = await testScope.Database.Spaces.ToListAsyncEF();
        
        var space = spaces.First(x => x.Id == spaceId);
        Assert.Equal("Space 1", space.Name);
        Assert.Equal("#ffffff", space.Color);
        Assert.Equal("SPB", space.Key);
        Assert.Equal(userId, space.CreatorId);
        Assert.True(space.CreatedAt != default);
        Assert.True(space.UpdatedAt != default);
    }
    
    [Fact]
    public async Task User_ShouldDeletePersonalSpace_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        
        var organization = await testScope.InitializePersonalOrganization(userId, org => org
            .AddSpace(userId));

        var spaceId = organization.Spaces![1].Id;
        
        await _spacesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Delete(spaceId));

        var spaces = await testScope.Database.Spaces.ToListAsyncEF();
        
        var space = spaces.FirstOrDefault(x => x.Id == spaceId);
        Assert.Null(space);
    }
    
    [Fact]
    public async Task User_ShouldViewPersonalOrganizationSpaces_Always()
    {
        using var testScope = host.CreateTestScope();
        
        // User 1 has organization with default space + additional space
        var user1Id = await testScope.CreateUser();
        var organization1 = await testScope.InitializePersonalOrganization(user1Id, org => org
            .AddSpace(user1Id));
        var organization1SpaceKeys = organization1.Spaces!.Select(x => x.Key);
        
        // User 2 has organization with default space
        var user2Id = await testScope.CreateUser();
        var organization2 = await testScope.InitializePersonalOrganization(user2Id);
        var organization2SpaceKeys = organization2.Spaces!.Select(x => x.Key);
        
        // User 1 see two spaces
        var spaces = await _spacesController
            .WithOrganizationAuthorization(organization1.Id, user1Id)
            .Execute(x => x.GetAll());
        Assert.Equal(2, spaces!.Length);
        Assert.Equivalent(organization1SpaceKeys, spaces.Select(x => x.Key));
        
        // User 2 see one space
        spaces = await _spacesController
            .WithOrganizationAuthorization(organization2.Id, user2Id)
            .Execute(x => x.GetAll());
        Assert.Single(spaces!);
        Assert.Equivalent(organization2SpaceKeys, spaces!.Select(x => x.Key));
    }
    
    [Fact]
    public async Task User_ShouldViewAllFieldsOrPersonalSpace_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(userId, org => org
            .AddSpace(userId,  space => space
                .WithColor("#ff11ff")
                .WithName("My Space")));
        var spaceKey = organization.Spaces![1].Key;
        
        var spaces = await _spacesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetAll());
        
        var space = spaces!.First(x => x.Key == spaceKey);
        Assert.Equal("#ff11ff", space.Color);
        Assert.Equal("My Space", space.Name);
    }
    
    [Fact]
    public async Task User_ShouldViewPersonalEpics_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            setup => setup
                .AddSpace(userId, spaceBuilder =>
                    spaceBuilder
                        .AddEpic(userId, epicBuilder => epicBuilder.WithName("My Epic").WithColor("#111111")
                            .AddStatus(s => s.WithName("In Progress"))
                            .AddIssue(userId, 1))));
        
        var space = organization.Spaces![1];
        var epicId = space.Epics![1].Id;
        
        var epics = await _spacesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetSpaceEpics(space.Id));
        
        Assert.Equal(2, epics!.Length);
        var epic = epics.FirstOrDefault(e => e.Id == epicId);
        
        Assert.False(epic!.IsDefault);
        Assert.Equal("#111111", epic.Color);
    }
    
    [Fact]
    public async Task User_ShouldNotViewSomeonePersonalEpics_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            setup => setup
                .AddSpace(userId, spaceBuilder =>
                    spaceBuilder
                        .AddEpic(userId)));
        
        var nonPermittedUserId = await testScope.CreateUser();
        var secondOrganization = await testScope.InitializePersonalOrganization(nonPermittedUserId);
        
        var space = organization.Spaces![1];

        var epics = await _spacesController
            .WithOrganizationAuthorization(secondOrganization.Id, nonPermittedUserId)
            .Execute(x => x.GetSpaceEpics(space.Id));
        
        Assert.Empty(epics!);
    }
}