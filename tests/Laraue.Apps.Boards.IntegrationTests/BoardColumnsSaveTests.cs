using System.Net;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// A board's columns saved with the rest of its settings, in one request.
/// </summary>
[Collection("IntegrationTest")]
public class BoardColumnsSaveTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<EpicsController> _epicsController = host.Controller<EpicsController>();

    [Fact]
    public async Task UpdateEpic_ShouldReplaceColumnsInGivenOrder_WhenColumnsAreSent()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var sprint = Sprint(organization);
        var first = StatusId(sprint, 0);
        var doing = StatusId(sprint, 1);
        var done = StatusId(sprint, 2);
        var issueId = sprint.Statuses![1].Issues!.Single().Id;

        await UpdateEpicAsync(organization.Id, ownerId, sprint.Id, "Sprint 2",
        [
            Column(done, "Finished", StatusCategory.Completed),
            Column(first, "To do", StatusCategory.Created),
            Column(null, "Review", StatusCategory.Review),
        ]);

        var columns = await testScope.Database.Statuses
            .AsNoTracking()
            .Where(x => x.EpicId == sprint.Id && x.DeletedAt == null)
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.Id, x.Name, x.Category })
            .ToListAsync();
        Assert.Equal(["Finished", "To do", "Review"], columns.Select(x => x.Name));
        Assert.Equal(done, columns[0].Id);
        Assert.Equal(StatusCategory.Review, columns[2].Category);
        Assert.NotNull((await testScope.Database.Statuses.AsNoTracking().SingleAsync(x => x.Id == doing)).DeletedAt);
        Assert.NotNull((await testScope.Database.Issues.AsNoTracking().SingleAsync(x => x.Id == issueId)).DeletedAt);
        Assert.Equal("Sprint 2", (await testScope.Database.Epics.AsNoTracking().SingleAsync(x => x.Id == sprint.Id)).Name);
    }

    [Fact]
    public async Task UpdateEpic_ShouldReturnBadRequestAndChangeNothing_WhenColumnIsAnotherBoardsStatus()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var sprint = Sprint(organization);
        var otherBoardStatus = Space(organization).Epics!.Single(x => x.IsDefault).Statuses![0].Id;

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => UpdateEpicAsync(
            organization.Id,
            ownerId,
            sprint.Id,
            "Renamed",
            [Column(otherBoardStatus, "Stolen", StatusCategory.Created)]));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("Sprint", (await testScope.Database.Epics.AsNoTracking().SingleAsync(x => x.Id == sprint.Id)).Name);
        Assert.Equal(
            3,
            await testScope.Database.Statuses.CountAsync(x => x.EpicId == sprint.Id && x.DeletedAt == null));
    }

    [Fact]
    public async Task UpdateEpic_ShouldReturnBadRequest_WhenNoColumnIsLeft()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => UpdateEpicAsync(organization.Id, ownerId, Sprint(organization).Id, "Sprint", []));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateEpic_ShouldKeepColumns_WhenColumnsAreNotSent()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var sprintId = Sprint(organization).Id;

        await UpdateEpicAsync(organization.Id, ownerId, sprintId, "Sprint 2", columns: null);

        Assert.Equal(3, await testScope.Database.Statuses.CountAsync(x => x.EpicId == sprintId && x.DeletedAt == null));
    }

    private const string SpaceKey = "COL";

    /// <summary>
    /// A space with a "Sprint" board: its default column, "Doing" with one issue, and "Done".
    /// </summary>
    private static Task<Organization> InitializeAsync(WebApiTestHostScope testScope, Guid ownerId)
    {
        return testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, SpaceKey, space => space
                .AddEpic(ownerId, epic => epic
                    .WithName("Sprint")
                    .AddStatus(s => s.WithName("Doing").WithCategory(StatusCategory.InProgress))
                    .AddStatus(s => s.WithName("Done").WithCategory(StatusCategory.Completed))
                    .AddIssue(ownerId, 1))));
    }

    private static Space Space(Organization organization)
    {
        return organization.Spaces!.Single(x => x.Key == SpaceKey);
    }

    private static Epic Sprint(Organization organization)
    {
        return Space(organization).Epics!.Single(x => x.Name == "Sprint");
    }

    private static long StatusId(Epic epic, int index)
    {
        return epic.Statuses![index].Id;
    }

    private static UpdateEpicColumn Column(long? id, string name, StatusCategory category)
    {
        return new UpdateEpicColumn { Id = id, Name = name, Color = "#4774d4", Category = category };
    }

    private Task UpdateEpicAsync(
        long organizationId,
        Guid userId,
        long epicId,
        string name,
        UpdateEpicColumn[]? columns)
    {
        var request = new UpdateEpicRequest { Name = name, Color = "#121212", Columns = columns };
        return _epicsController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.Update(epicId, request, CancellationToken.None));
    }
}
