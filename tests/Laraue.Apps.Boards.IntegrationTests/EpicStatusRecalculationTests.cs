using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Migrations;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// An epic's status is derived from its active issues (BRD-323): New with none or all of them Created, Done with
/// all of them Completed, Active otherwise. The seeded epic (space 1, epic 1) has a status per category, in
/// <see cref="StatusIndexes"/> order; seeding writes issues directly, so the epic starts as New whatever they are.
/// </summary>
[Collection("IntegrationTest")]
public class EpicStatusRecalculationTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<StatusesController> _statusesController = host.Controller<StatusesController>();

    private static readonly Dictionary<StatusCategory, int> StatusIndexes = new()
    {
        [StatusCategory.Created] = 0,
        [StatusCategory.InProgress] = 1,
        [StatusCategory.Review] = 2,
        [StatusCategory.Completed] = 3,
    };

    [Fact]
    public async Task Recalculate_ShouldSetNew_WhenEpicHasNoIssues()
    {
        Assert.Equal(EpicStatus.New, await RecalculateSeeded());
    }

    [Fact]
    public async Task Recalculate_ShouldSetNew_WhenAllIssuesAreCreated()
    {
        Assert.Equal(EpicStatus.New, await RecalculateSeeded(StatusCategory.Created, StatusCategory.Created));
    }

    [Fact]
    public async Task Recalculate_ShouldSetActive_WhenAnyIssueIsInProgress()
    {
        Assert.Equal(EpicStatus.Active, await RecalculateSeeded(StatusCategory.Created, StatusCategory.InProgress));
    }

    [Fact]
    public async Task Recalculate_ShouldSetActive_WhenAnyIssueIsInReview()
    {
        Assert.Equal(EpicStatus.Active, await RecalculateSeeded(StatusCategory.Completed, StatusCategory.Review));
    }

    [Fact]
    public async Task Recalculate_ShouldSetActive_WhenCreatedAndCompletedIssuesAreMixed()
    {
        Assert.Equal(EpicStatus.Active, await RecalculateSeeded(StatusCategory.Created, StatusCategory.Completed));
    }

    [Fact]
    public async Task Recalculate_ShouldSetDone_WhenAllIssuesAreCompleted()
    {
        Assert.Equal(EpicStatus.Done, await RecalculateSeeded(StatusCategory.Completed, StatusCategory.Completed));
    }

    [Fact]
    public async Task Recalculate_ShouldIgnoreDeletedIssues_WhenTheRestAreCompleted()
    {
        using var testScope = host.CreateTestScope();
        var seeded = await SeedEpic(testScope, StatusCategory.Completed, StatusCategory.InProgress);
        await testScope.Database.Issues
            .Where(x => x.Id == seeded.Organization.GetIssueData(1, 1, StatusIndexes[StatusCategory.InProgress], 0).Issue.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.DeletedAt, DateTime.UtcNow));

        await testScope.Database.RecalculateEpicStatuses([seeded.EpicId], CancellationToken.None);

        Assert.Equal(EpicStatus.Done, await GetEpicStatus(testScope, seeded.EpicId));
    }

    [Fact]
    public async Task Create_ShouldMakeEpicActive_WhenIssueIsCreatedInProgress()
    {
        using var testScope = host.CreateTestScope();
        var seeded = await SeedEpic(testScope);
        var statusId = seeded.Organization.GetStatus(1, 1, StatusIndexes[StatusCategory.InProgress]).Id;

        await CreateIssue(testScope, seeded.UserId, statusId);

        Assert.Equal(EpicStatus.Active, await GetEpicStatus(testScope, seeded.EpicId));
    }

    [Fact]
    public async Task UpdateIssuesStatus_ShouldMakeEpicDone_WhenLastOpenIssueIsCompleted()
    {
        using var testScope = host.CreateTestScope();
        var seeded = await SeedEpic(testScope, StatusCategory.Completed, StatusCategory.InProgress);
        var issueId = seeded.Organization.GetIssueData(1, 1, StatusIndexes[StatusCategory.InProgress], 0).Issue.Id;
        var completedStatusId = seeded.Organization.GetStatus(1, 1, StatusIndexes[StatusCategory.Completed]).Id;

        await MoveIssue(testScope, seeded.UserId, issueId, completedStatusId);

        Assert.Equal(EpicStatus.Done, await GetEpicStatus(testScope, seeded.EpicId));
    }

    [Fact]
    public async Task UpdateIssuesStatus_ShouldRecalculateBothEpics_WhenIssueMovesToAnotherEpic()
    {
        using var testScope = host.CreateTestScope();
        var seeded = await SeedEpic(testScope, StatusCategory.InProgress);
        var issueId = seeded.Organization.GetIssueData(1, 1, StatusIndexes[StatusCategory.InProgress], 0).Issue.Id;
        // The space's backlog epic, seeded empty with a single Created status; marked Done so the change shows.
        var backlogStatus = seeded.Organization.GetStatus(1, 0, 0);
        await testScope.Database.RecalculateEpicStatuses([seeded.EpicId], CancellationToken.None);
        await testScope.Database.Epics
            .Where(x => x.Id == backlogStatus.EpicId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, EpicStatus.Done));

        await MoveIssue(testScope, seeded.UserId, issueId, backlogStatus.Id);

        Assert.Equal(EpicStatus.New, await GetEpicStatus(testScope, seeded.EpicId));
        Assert.Equal(EpicStatus.New, await GetEpicStatus(testScope, backlogStatus.EpicId));
    }

    [Fact]
    public async Task Delete_ShouldMakeEpicNew_WhenOnlyStartedIssueIsDeleted()
    {
        using var testScope = host.CreateTestScope();
        var seeded = await SeedEpic(testScope, StatusCategory.Created, StatusCategory.InProgress);
        var issueId = seeded.Organization.GetIssueData(1, 1, StatusIndexes[StatusCategory.InProgress], 0).Issue.Id;
        await testScope.Database.RecalculateEpicStatuses([seeded.EpicId], CancellationToken.None);

        await testScope.Services.GetRequiredService<ICoreIssuesService>()
            .Delete(issueId, new Actor(seeded.UserId), CancellationToken.None);

        Assert.Equal(EpicStatus.New, await GetEpicStatus(testScope, seeded.EpicId));
    }

    [Fact]
    public async Task EditStatus_ShouldRecalculateEpic_WhenCategoryChanges()
    {
        using var testScope = host.CreateTestScope();
        var seeded = await SeedEpic(testScope, StatusCategory.Created, StatusCategory.Completed);
        var createdStatusId = seeded.Organization.GetStatus(1, 1, StatusIndexes[StatusCategory.Created]).Id;
        await testScope.Database.RecalculateEpicStatuses([seeded.EpicId], CancellationToken.None);

        await _statusesController
            .WithOrganizationAuthorization(seeded.Organization.Id, seeded.UserId)
            .Execute(x => x.Edit(
                createdStatusId,
                new EditStatusRequest
                {
                    Name = "Closed",
                    Color = "#111111",
                    Category = StatusCategory.Completed,
                },
                CancellationToken.None));

        Assert.Equal(EpicStatus.Done, await GetEpicStatus(testScope, seeded.EpicId));
    }

    [Fact]
    public async Task DeleteStatus_ShouldRecalculateEpic_WhenItsIssuesAreDeleted()
    {
        using var testScope = host.CreateTestScope();
        var seeded = await SeedEpic(testScope, StatusCategory.Completed, StatusCategory.InProgress);
        var inProgressStatusId = seeded.Organization.GetStatus(1, 1, StatusIndexes[StatusCategory.InProgress]).Id;
        await testScope.Database.RecalculateEpicStatuses([seeded.EpicId], CancellationToken.None);

        await _statusesController
            .WithOrganizationAuthorization(seeded.Organization.Id, seeded.UserId)
            .Execute(x => x.Delete(inProgressStatusId, CancellationToken.None));

        Assert.Equal(EpicStatus.Done, await GetEpicStatus(testScope, seeded.EpicId));
    }

    [Fact]
    public async Task Migration_ShouldRecalculateEpicStatus_WhenSetByHand()
    {
        using var testScope = host.CreateTestScope();
        var seeded = await SeedEpic(testScope, StatusCategory.Created, StatusCategory.InProgress);
        await testScope.Database.Epics
            .Where(x => x.Id == seeded.EpicId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, EpicStatus.Done));

        // The migration touches every epic, so it runs in a transaction that is rolled back.
        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();
        await testScope.Database.Database.ExecuteSqlRawAsync(RecalculateEpicStatuses.RecalculateSql);
        var status = await GetEpicStatus(testScope, seeded.EpicId);
        await transaction.RollbackAsync();

        Assert.Equal(EpicStatus.Active, status);
    }

    private async Task<EpicStatus> RecalculateSeeded(params StatusCategory[] issueCategories)
    {
        using var testScope = host.CreateTestScope();
        var seeded = await SeedEpic(testScope, issueCategories);

        await testScope.Database.RecalculateEpicStatuses([seeded.EpicId], CancellationToken.None);

        return await GetEpicStatus(testScope, seeded.EpicId);
    }

    private static async Task<SeededEpic> SeedEpic(
        WebApiTestHostScope testScope,
        params StatusCategory[] issueCategories)
    {
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, org => org
            .AddSpace(userId, space => space
                .AddEpic(userId, epic =>
                {
                    epic
                        .AddStatus(s => s.WithName("In Progress").WithCategory(StatusCategory.InProgress))
                        .AddStatus(s => s.WithName("Review").WithCategory(StatusCategory.Review))
                        .AddStatus(s => s.WithName("Done").WithCategory(StatusCategory.Completed));

                    foreach (var category in issueCategories)
                        epic.AddIssue(userId, StatusIndexes[category]);
                })));

        return new SeededEpic(organization, userId, organization.GetEpic(1, 1).Id);
    }

    private static async Task<long> CreateIssue(WebApiTestHostScope testScope, Guid userId, long statusId)
    {
        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();
        var issueId = await testScope.Services.GetRequiredService<ICoreIssuesService>()
            .Create(
                new Actor(userId),
                new IssueCreateRequest(statusId, DateTime.UtcNow, "Issue", isTitleSetExplicitly: true),
                CancellationToken.None);
        await transaction.CommitAsync();

        return issueId;
    }

    private static async Task MoveIssue(WebApiTestHostScope testScope, Guid userId, long issueId, long statusId)
    {
        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();
        await testScope.Services.GetRequiredService<ICoreIssuesService>()
            .UpdateIssuesStatus([issueId], statusId, new Actor(userId), comment: null, CancellationToken.None);
        await transaction.CommitAsync();
    }

    private static Task<EpicStatus> GetEpicStatus(WebApiTestHostScope testScope, long epicId)
    {
        return testScope.Database.Epics
            .Where(x => x.Id == epicId)
            .Select(x => x.Status)
            .SingleAsyncEF();
    }

    private record SeededEpic(Organization Organization, Guid UserId, long EpicId);
}
