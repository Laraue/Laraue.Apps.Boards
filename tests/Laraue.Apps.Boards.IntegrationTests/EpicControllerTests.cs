using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.Exceptions.Web;
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
    public async Task User_ShouldCreateEpicWithCustomStatuses_WhenStatusesProvided()
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
                    Statuses = new[]
                    {
                        new CreateEpicStatusDto { Name = "To Do", Color = "#111111" },
                        new CreateEpicStatusDto { Name = "In Progress", Color = "#222222" },
                        new CreateEpicStatusDto { Name = "Done", Color = "#333333" },
                    },
                }));

        var epic = await testScope.Database.Epics
            .Include(e => e.Statuses)
            .FirstAsyncEF(x => x.Id == epicId);

        var statuses = epic.Statuses!.OrderBy(s => s.SortOrder).ToArray();
        Assert.Equal(3, statuses.Length);
        Assert.Equal("To Do", statuses[0].Name);
        Assert.Equal("#111111", statuses[0].Color);
        Assert.Equal(0, statuses[0].SortOrder);
        Assert.Equal("In Progress", statuses[1].Name);
        Assert.Equal(1, statuses[1].SortOrder);
        Assert.Equal("Done", statuses[2].Name);
        Assert.Equal(2, statuses[2].SortOrder);
    }

    [Fact]
    public async Task User_ShouldCreateEpicWithDefaultStatus_WhenStatusesIsEmpty()
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
                    Statuses = Array.Empty<CreateEpicStatusDto>(),
                }));

        var epic = await testScope.Database.Epics
            .Include(e => e.Statuses)
            .FirstAsyncEF(x => x.Id == epicId);

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
        Assert.Equal(EpicStatus.New, epic.Status);
    }

    [Fact]
    public async Task User_ShouldChangeEpicStatusInOrganization_WhenHasDirectEditAccess()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(participatorId, builder => builder
                .SetSpaceAccessLevel(0, x => x.CanUpdateEpics = true)));

        var epicId = organization.GetEpic(0, 0).Id;

        await _epicsController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.ChangeStatus(
                epicId,
                new ChangeEpicStatusRequest
                {
                    Status = EpicStatus.Active,
                }));

        var epic = await testScope.Database.Epics.FirstAsyncEF(x => x.Id == epicId);
        Assert.Equal(EpicStatus.Active, epic.Status);
    }

    [Fact]
    public async Task User_ShouldNotChangeEpicStatusInOrganization_WhenHasNoDirectEditAccess()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(participatorId, builder => builder
                .SetGlobalAccessLevel(x => x.CanRead = true)));

        var epicId = organization.GetEpic(0, 0).Id;

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _epicsController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.ChangeStatus(
                epicId,
                new ChangeEpicStatusRequest
                {
                    Status = EpicStatus.Done,
                })));

        var forbidden = ex.HasInnerException<ForbiddenException>();
        Assert.Equal($"Epic: {epicId} is not accessible", forbidden.Message);
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

    [Fact]
    public async Task User_ShouldSearchEpicsWithStatuses_AcrossAllAccessibleSpaces_WhenSpaceKeyNotProvided()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId, u => u.SetGlobalAccessLevel(x => x.CanRead = true))
                .AddSpace(userId, "SPA", space => space
                    .AddEpic(userId, e => e
                        .WithName("Sprint Board")
                        .AddStatus(s => s.WithName("To Do").WithColor("#111111"))
                        .AddStatus(s => s.WithName("Done").WithColor("#222222")))));

        var result = await _epicsController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.SearchEpicsWithStatuses(new SearchEpicStatusesRequest
            {
                Pagination = new PaginationData { Page = 0, PerPage = 10 },
            }));

        Assert.NotNull(result);
        // The default "DEF" space's own Backlog epic, plus the auto-created Backlog epic that
        // any additional space gets, plus the explicitly added "Sprint Board" epic.
        Assert.Equal(3, result.Data.Count);

        // The builder always seeds one extra default "New" status on epics in non-first spaces,
        // in addition to whatever is added explicitly below.
        var sprintBoard = Assert.Single(result.Data, x => x.EpicName == "Sprint Board");
        Assert.Contains(sprintBoard.Statuses, s => s.Name == "To Do");
        Assert.Contains(sprintBoard.Statuses, s => s.Name == "Done");
    }

    [Fact]
    public async Task User_ShouldFilterEpicsWithStatusesBySpaceKey_WhenSpaceKeyProvided()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o.AddSpace(userId, "SPA", space => space
                .AddEpic(userId, e => e.WithName("Sprint Board"))));

        var result = await _epicsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.SearchEpicsWithStatuses(new SearchEpicStatusesRequest
            {
                SpaceKey = "SPA",
                Pagination = new PaginationData { Page = 0, PerPage = 10 },
            }));

        Assert.NotNull(result);
        // "SPA" auto-gets its own default Backlog epic in addition to the explicit "Sprint Board"
        // one; neither the default "DEF" space's Backlog epic nor any other space should appear.
        Assert.Equal(2, result.Data.Count);
        Assert.Single(result.Data, x => x.EpicName == "Sprint Board");
    }

    [Fact]
    public async Task User_ShouldFilterEpicsWithStatusesBySearchString_WhenSearchStringProvided()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o.AddSpace(userId, "SPA", space => space
                .AddEpic(userId, e => e.WithName("Sprint Board"))));

        var result = await _epicsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.SearchEpicsWithStatuses(new SearchEpicStatusesRequest
            {
                SearchString = "sprint",
                Pagination = new PaginationData { Page = 0, PerPage = 10 },
            }));

        Assert.NotNull(result);
        Assert.Single(result.Data, x => x.EpicName == "Sprint Board");
    }

    [Fact]
    public async Task User_ShouldReturnEmptyEpicsWithStatuses_WhenSpaceKeyDoesNotExist()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, org => org
            .AddSpace(userId));

        var result = await _epicsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.SearchEpicsWithStatuses(new SearchEpicStatusesRequest
            {
                SpaceKey = "ZZZ",
                Pagination = new PaginationData { Page = 0, PerPage = 10 },
            }));

        Assert.NotNull(result);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task User_ShouldReturnEmptyEpicsWithStatuses_WhenUserHasNoAccessToSpace()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId, u => u.SetSpaceAccessLevel(0, x => x.CanRead = true))
                .AddSpace(userId, "SPA", space => space
                    .AddEpic(userId, e => e.WithName("Hidden Board"))));

        var result = await _epicsController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.SearchEpicsWithStatuses(new SearchEpicStatusesRequest
            {
                SpaceKey = "SPA",
                Pagination = new PaginationData { Page = 0, PerPage = 10 },
            }));

        Assert.NotNull(result);
        Assert.Empty(result.Data);
    }
}
