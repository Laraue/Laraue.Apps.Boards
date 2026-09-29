using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Migrations;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Runs <see cref="AddStatusCategory"/>'s backfill on seeded statuses - the migration itself ran on an
/// empty test database. The backfill touches every status, so it runs in a transaction that is rolled
/// back, leaving the other tests' data as it was.
/// </summary>
[Collection("IntegrationTest")]
public class StatusCategoryBackfillTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task Backfill_ShouldCategorizeStatusesByColumnPosition_WhenEpicHasSeveralStatuses()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, org => org
            .AddSpace(userId, space => space
                .AddEpic(userId, epic => epic
                    .AddStatus(s => s.WithName("Done"))
                    .AddStatus(s => s.WithName("To Do"))
                    .AddStatus(s => s.WithName("Review")))));

        // Index 0 is the epic's implicit "New" status - deleted here, placed after every column.
        var deleted = organization.GetStatus(1, 1, 0);
        var done = organization.GetStatus(1, 1, 1);
        var toDo = organization.GetStatus(1, 1, 2);
        var review = organization.GetStatus(1, 1, 3);
        await SetStatus(testScope, deleted.Id, sortOrder: 3, StatusCategory.Created, isDeleted: true);
        await SetStatus(testScope, done.Id, sortOrder: 2, StatusCategory.Created);
        await SetStatus(testScope, toDo.Id, sortOrder: 0, StatusCategory.Completed);
        await SetStatus(testScope, review.Id, sortOrder: 1, StatusCategory.Created);

        var categories = await RunBackfill(testScope, deleted.Id, done.Id, toDo.Id, review.Id);

        Assert.Equal(StatusCategory.Created, categories[toDo.Id]);
        Assert.Equal(StatusCategory.InProgress, categories[review.Id]);
        Assert.Equal(StatusCategory.Completed, categories[done.Id]);
        Assert.Equal(StatusCategory.InProgress, categories[deleted.Id]);
    }

    [Fact]
    public async Task Backfill_ShouldKeepStatusCreated_WhenItIsTheOnlyStatusInEpic()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        // The organization's own default epic is seeded with exactly one status.
        var organization = await testScope.InitializeOrganization(userId);

        var status = organization.GetStatus(0, 0, 0);
        await SetStatus(testScope, status.Id, sortOrder: 0, StatusCategory.Completed);

        var categories = await RunBackfill(testScope, status.Id);

        Assert.Equal(StatusCategory.Created, categories[status.Id]);
    }

    private static Task SetStatus(
        WebApiTestHostScope testScope,
        long statusId,
        int sortOrder,
        StatusCategory category,
        bool isDeleted = false)
    {
        return testScope.Database.Statuses
            .Where(x => x.Id == statusId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.SortOrder, sortOrder)
                .SetProperty(x => x.Category, category)
                .SetProperty(x => x.DeletedAt, isDeleted ? DateTime.UtcNow : null));
    }

    private static async Task<Dictionary<long, StatusCategory>> RunBackfill(
        WebApiTestHostScope testScope,
        params long[] statusIds)
    {
        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();

        await testScope.Database.Database.ExecuteSqlRawAsync(AddStatusCategory.BackfillCategorySql);

        // The seeding context may still track these statuses with their pre-backfill values.
        var categories = await testScope.Database.Statuses
            .AsNoTracking()
            .Where(x => statusIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Category);

        await transaction.RollbackAsync();

        return categories;
    }
}
