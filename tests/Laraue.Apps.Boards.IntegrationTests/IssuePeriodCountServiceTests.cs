using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class IssuePeriodCountServiceTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private static readonly DateTime Period = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Increment_ShouldCreateTheRowAndThenAddOne_WhenCalledTwice()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        await IncrementAsync(organization.Id, Period);
        await IncrementAsync(organization.Id, Period);

        var service = new IssuePeriodCountService(testScope.Database);
        Assert.Equal(2, await service.GetCount(organization.Id, Period, CancellationToken.None));
    }

    [Fact]
    public async Task Increment_ShouldCountEachPeriodOnItsOwn_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        await IncrementAsync(organization.Id, Period);
        await IncrementAsync(organization.Id, Period.AddMonths(1));
        await IncrementAsync(organization.Id, Period.AddMonths(1));

        var service = new IssuePeriodCountService(testScope.Database);
        Assert.Equal(1, await service.GetCount(organization.Id, Period, CancellationToken.None));
        Assert.Equal(2, await service.GetCount(organization.Id, Period.AddMonths(1), CancellationToken.None));
        Assert.Equal(0, await service.GetCount(organization.Id, Period.AddMonths(2), CancellationToken.None));
    }

    [Fact]
    public async Task Increment_ShouldNotLoseACount_WhenManyIssuesAreCreatedAtOnce()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        // Many callers at once, each on its own connection and transaction, and the row does not exist yet.
        const int callers = 16;
        await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => IncrementAsync(organization.Id, Period)));

        var count = await testScope.Database.IssuePeriodCounts
            .Where(x => x.OrganizationId == organization.Id && x.PeriodStartedAt == Period)
            .Select(x => x.Count)
            .ToListAsync();
        Assert.Equal([callers], count);
    }

    private async Task IncrementAsync(long organizationId, DateTime periodStartedAt)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var service = new IssuePeriodCountService(context);

        await using var transaction = await context.Database.BeginTransactionAsync();
        await service.Increment(organizationId, periodStartedAt, CancellationToken.None);
        await transaction.CommitAsync();
    }
}
