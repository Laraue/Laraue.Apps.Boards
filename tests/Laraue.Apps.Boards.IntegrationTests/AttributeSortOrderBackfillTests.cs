using Laraue.Apps.Boards.DataAccess.Migrations;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Runs <see cref="AddAttributeSettings"/>'s sort order backfill on seeded attributes - the migration itself ran on an
/// empty test database. The backfill touches every attribute, so it runs in a transaction that is rolled back, leaving
/// the other tests' data as it was.
/// </summary>
[Collection("IntegrationTest")]
public class AttributeSortOrderBackfillTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task Backfill_ShouldOrderAttributesByCreationWithinEachOrganization_WhenAttributesExist()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var first = await testScope.InitializeOrganization(userId, org => org
            .AddTextAttribute("A")
            .AddTextAttribute("B")
            .AddTextAttribute("C"));
        var second = await testScope.InitializeOrganization(userId, org => org
            .AddTextAttribute("D")
            .AddTextAttribute("E"));
        long[] organizationIds = [first.Id, second.Id];

        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();
        await testScope.Database.Attributes
            .Where(x => organizationIds.Contains(x.OrganizationId))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.SortOrder, 0));

        await testScope.Database.Database.ExecuteSqlRawAsync(AddAttributeSettings.BackfillSortOrderSql);

        var sortOrders = await testScope.Database.Attributes
            .AsNoTracking()
            .Where(x => organizationIds.Contains(x.OrganizationId))
            .ToDictionaryAsync(x => x.Name, x => x.SortOrder);
        await transaction.RollbackAsync();

        Assert.Equal(0, sortOrders["A"]);
        Assert.Equal(1, sortOrders["B"]);
        Assert.Equal(2, sortOrders["C"]);
        Assert.Equal(0, sortOrders["D"]);
        Assert.Equal(1, sortOrders["E"]);
    }
}
