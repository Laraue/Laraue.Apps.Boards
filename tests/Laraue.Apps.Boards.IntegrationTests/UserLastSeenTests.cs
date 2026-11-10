using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Migrations;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Retro.WebApiHost.Controllers;
using Laraue.Apps.Retro.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// <see cref="User.LastSeenAt"/> (BRD-348) is set by every way a user reaches Boards - web, retro and MCP here,
/// Telegram in <see cref="UserLastSeenTelegramTests"/> - and written at most once an hour.
/// </summary>
[Collection("IntegrationTest")]
public class UserLastSeenTests(WebApiTestHost host, RetroWebApiTestHost retroHost, McpHostTestHost mcpHost)
    : IClassFixture<WebApiTestHost>, IClassFixture<RetroWebApiTestHost>, IClassFixture<McpHostTestHost>
{
    [Fact]
    public async Task GetUser_ShouldSetLastSeenAt_WhenUserTokenIsValid()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();

        await host
            .Controller<UserController>()
            .WithUserAuthorization(userId)
            .Execute(x => x.GetAsync(default));

        Assert.NotNull(await GetLastSeenAt(testScope, userId));
    }

    [Fact]
    public async Task GetSpaces_ShouldSetLastSeenAt_WhenOrganizationTokenIsValid()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        await host
            .Controller<SpacesController>()
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetAll(default));

        Assert.NotNull(await GetLastSeenAt(testScope, userId));
    }

    [Fact]
    public async Task GetRetros_ShouldSetLastSeenAt_WhenOrganizationTokenIsValid()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        await retroHost
            .Controller<RetroController>(host.Services)
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Get(
                new GetRetrosRequest { Pagination = new PaginationData { Page = 0, PerPage = 10 } },
                default));

        Assert.NotNull(await GetLastSeenAt(testScope, userId));
    }

    [Fact]
    public async Task McpCall_ShouldSetLastSeenAt_WhenApiKeyIsValid()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var apiKey = await testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(organization.Id, userId, "Test key", CancellationToken.None);

        await using (var client = await mcpHost.ConnectMcpClientAsync(apiKey.RawKey))
        {
            await client.CallToolAsync("list_spaces");
        }

        Assert.NotNull(await GetLastSeenAt(testScope, userId));
    }

    [Fact]
    public async Task Touch_ShouldKeepLastSeenAt_WhenItWasWrittenWithinTheInterval()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var recently = DateTime.UtcNow.AddMinutes(-30);
        await SetLastSeenAt(testScope, userId, recently);

        await Touch(userId);

        // Postgres keeps microseconds, so compare at that precision.
        var lastSeenAt = await GetLastSeenAt(testScope, userId);
        Assert.NotNull(lastSeenAt);
        Assert.Equal(recently, lastSeenAt.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Touch_ShouldUpdateLastSeenAt_WhenItIsOlderThanTheInterval()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var longAgo = DateTime.UtcNow.AddHours(-2);
        await SetLastSeenAt(testScope, userId, longAgo);

        await Touch(userId);

        Assert.True(await GetLastSeenAt(testScope, userId) > longAgo.AddHours(1));
    }

    [Fact]
    public async Task Backfill_ShouldSetLastSeenAtToLatestHistoryEntry_WhenUserHasHistory()
    {
        using var testScope = host.CreateTestScope();
        var withHistory = await testScope.CreateUser();
        var withoutHistory = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(withHistory);

        var older = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var latest = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc);
        testScope.Database.OrganizationLogs.AddRange(
            CreateLog(organization.Id, withHistory, older),
            CreateLog(organization.Id, withHistory, latest));
        await testScope.Database.SaveChangesAsync();

        // The backfill touches every user, so it runs in a transaction that is rolled back.
        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();
        await testScope.Database.Users
            .Where(x => x.Id == withHistory || x.Id == withoutHistory)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, (DateTime?)null));

        await testScope.Database.Database.ExecuteSqlRawAsync(AddUserLastSeenAt.BackfillSql);

        Assert.Equal(latest, await GetLastSeenAt(testScope, withHistory));
        Assert.Null(await GetLastSeenAt(testScope, withoutHistory));

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// A fresh scope per call, as each request gets; the per-host cache is shared, but these users are new to it.
    /// </summary>
    private async Task Touch(Guid userId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<ICoreUserLastSeenService>()
            .Touch(userId, CancellationToken.None);
    }

    private static Task SetLastSeenAt(WebApiTestHostScope testScope, Guid userId, DateTime value)
    {
        return testScope.Database.Users
            .Where(x => x.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, value));
    }

    private static Task<DateTime?> GetLastSeenAt(WebApiTestHostScope testScope, Guid userId)
    {
        return testScope.Database.Users
            .Where(x => x.Id == userId)
            .Select(x => x.LastSeenAt)
            .SingleAsync();
    }

    private static OrganizationLog CreateLog(long organizationId, Guid userId, DateTime createdAt)
    {
        return new OrganizationLog
        {
            OrganizationId = organizationId,
            OwnerId = userId,
            EntityType = LogEntityType.Issue,
            Action = LogAction.Create,
            CreatedAt = createdAt,
        };
    }
}
