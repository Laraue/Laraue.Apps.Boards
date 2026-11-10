using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Metrics;
using Laraue.Apps.Retro.WebApiHost.Controllers;
using Laraue.Apps.Retro.WebApiServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Scrapes the hosts' Prometheus endpoints after real operations. Meters are process-wide, so another test can add
/// to the same series - assert that a series exists, not its exact count.
/// </summary>
[Collection("IntegrationTest")]
public class BoardsMetricsTests(WebApiTestHost host, RetroWebApiTestHost retroHost)
    : IClassFixture<WebApiTestHost>, IClassFixture<RetroWebApiTestHost>
{
    [Fact]
    public async Task Metrics_ShouldExposeIssuesCreatedBySource_WhenIssuesAreCreatedByWebAndApiKey()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        await CreateIssueAsync(testScope, organization, new Actor(userId));
        var apiKey = await testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(organization.Id, userId, "Metrics key", CancellationToken.None);
        await CreateIssueAsync(testScope, organization, new Actor(userId, apiKey.Id));

        var metrics = await ScrapeAsync(host.CreateClient());

        Assert.Contains(SeriesLines(metrics, "boards_issues_created_total"), line => line.Contains("source=\"web\""));
        Assert.Contains(SeriesLines(metrics, "boards_issues_created_total"), line => line.Contains("source=\"mcp\""));
    }

    [Fact]
    public async Task Metrics_ShouldExposeEverySeriesFromStart_WhenNoEventHappenedYet()
    {
        // No test creates a Telegram issue or deletes one in a way that matters here - the series exist only
        // because the counters record a zero for every known label value on start.
        var metrics = await ScrapeAsync(host.CreateClient());

        Assert.Contains(SeriesLines(metrics, "boards_issues_created_total"), line => line.Contains("source=\"telegram\""));
        Assert.Contains(SeriesLines(metrics, "boards_organizations_created_total"), line => line.Contains("type=\"personal\""));
        Assert.NotEmpty(SeriesLines(metrics, "boards_issues_completed_total"));
        Assert.NotEmpty(SeriesLines(metrics, "boards_issues_deleted_total"));
        Assert.Contains(SeriesLines(metrics, "boards_organization_setups_completed_total"), line => line.Contains("demo_data=\"added\""));
    }

    [Fact]
    public async Task Metrics_ShouldExposeDeletedIssues_WhenIssueIsDeleted()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var issueId = await CreateIssueAsync(testScope, organization, new Actor(userId));
        await CreateIssueAsync(testScope, organization, new Actor(userId));

        await using (var transaction = await testScope.Database.Database.BeginTransactionAsync())
        {
            await testScope.Services.GetRequiredService<ICoreIssuesService>()
                .Delete(issueId, new Actor(userId), CancellationToken.None);
            await transaction.CommitAsync();
        }

        await host.Services.GetRequiredService<BoardsStateMetrics>().RefreshAsync(CancellationToken.None);
        var metrics = await ScrapeAsync(host.CreateClient());

        Assert.Contains(SeriesLines(metrics, "boards_issues_deleted_total"), line => !line.EndsWith(" 0"));
        Assert.Equal("1", GaugeValue(metrics, "boards_issues_soft_deleted"));
    }

    [Fact]
    public async Task Metrics_ShouldExposeCompletedIssues_WhenIssueIsMovedToCompletedStatus()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var openStatus = organization.GetStatus(0, 0, 0);
        var doneStatus = new DataAccess.Models.Status
        {
            Name = "Done",
            Color = "#2f9e44",
            EpicId = openStatus.EpicId,
            SortOrder = 99,
            Category = StatusCategory.Completed,
        };
        testScope.Database.Statuses.Add(doneStatus);
        await testScope.Database.SaveChangesAsync();
        var issueId = await CreateIssueAsync(testScope, organization, new Actor(userId), openStatus.Id);

        await using (var transaction = await testScope.Database.Database.BeginTransactionAsync())
        {
            await testScope.Services.GetRequiredService<ICoreIssuesService>()
                .UpdateIssuesStatus([issueId], doneStatus.Id, new Actor(userId), comment: null, CancellationToken.None);
            await transaction.CommitAsync();
        }

        var metrics = await ScrapeAsync(host.CreateClient());

        Assert.NotEmpty(SeriesLines(metrics, "boards_issues_completed_total"));
    }

    [Fact]
    public async Task Metrics_ShouldExposeCreatedOrganizations_WhenOrganizationIsCreated()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();

        await testScope.Services.GetRequiredService<ICoreOrganizationsService>()
            .Create(userId, "metrics-org", "Metrics org", "#4774d4", CancellationToken.None);

        var metrics = await ScrapeAsync(host.CreateClient());

        Assert.Contains(SeriesLines(metrics, "boards_organizations_created_total"), line =>
            line.Contains("type=\"organization\""));
    }

    [Fact]
    public async Task Metrics_ShouldExposeStartedRetros_WhenRetroIsCreated()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);

        await retroHost.Controller<RetroController>(host.Services)
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.Create(new CreateRetroRequest { Name = "Metrics retro" }));

        var metrics = await ScrapeAsync(retroHost.CreateClient());

        Assert.NotEmpty(SeriesLines(metrics, "boards_retros_started_total"));
    }

    [Fact]
    public async Task StateMetrics_ShouldExposeTotalsAndActiveUsers_WhenRefreshed()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        await CreateIssueAsync(testScope, organization, new Actor(userId));
        // Another user who was never seen doesn't count as active.
        await testScope.CreateUser();
        await UpdateUser(testScope, userId, createdAt: DateTime.UtcNow.AddDays(-3), lastSeenAt: DateTime.UtcNow);

        await host.Services.GetRequiredService<BoardsStateMetrics>().RefreshAsync(CancellationToken.None);
        var metrics = await ScrapeAsync(host.CreateClient());

        Assert.Contains(SeriesLines(metrics, "boards_issues"), line => line.Contains("state=\"active\""));
        Assert.Contains(SeriesLines(metrics, "boards_issues"), line => line.Contains("state=\"completed\""));
        foreach (var status in new[] { "new", "active", "done" })
        {
            Assert.Contains(SeriesLines(metrics, "boards_epics"), line => line.Contains($"status=\"{status}\""));
        }

        Assert.Equal("1", GaugeValue(metrics, "boards_organizations", "type=\"organization\""));
        Assert.Contains(SeriesLines(metrics, "boards_organizations"), line => line.Contains("type=\"personal\""));
        Assert.Contains(SeriesLines(metrics, "boards_retros"), line => line.Contains("state=\"running\""));
        Assert.Contains(SeriesLines(metrics, "boards_retros"), line => line.Contains("state=\"finished\""));
        Assert.Equal("1", GaugeValue(metrics, "boards_active_users", "window=\"1d\""));
        Assert.Equal("1", GaugeValue(metrics, "boards_active_users", "window=\"30d\""));
    }

    [Fact]
    public async Task StateMetrics_ShouldCountFunnelStepsPerCohortAndMethod_WhenUsersReachedDifferentSteps()
    {
        using var testScope = host.CreateTestScope();
        var now = DateTime.UtcNow;

        // Signed up 10 days ago with Telegram and went all the way: a team organization set up, an issue of
        // their own, a teammate, and still coming back.
        var activeUserId = await testScope.CreateUser();
        var teammateId = await testScope.CreateUser();
        var teamOrganization = await testScope.InitializeOrganization(activeUserId, org => org.AddUser(teammateId));
        await SetOrganizationTimes(testScope, teamOrganization.Id, createdAt: now.AddDays(-10), setupCompletedAt: now.AddDays(-10).AddMinutes(5));
        await CreateIssueAsync(testScope, teamOrganization, new Actor(activeUserId));
        await UpdateUser(testScope, activeUserId, createdAt: now.AddDays(-10), lastSeenAt: now);

        // The teammate signed up long ago and did nothing themselves.
        await UpdateUser(testScope, teammateId, createdAt: now.AddDays(-60), lastSeenAt: null);

        // Signed up 2 days ago with Google, finished the setup with demo data and never came back: the demo
        // issue was created before the setup completed, so it isn't their first issue.
        var googleUserId = await testScope.CreateUser(user => user.TelegramId = null);
        var personalOrganization = await testScope.InitializePersonalOrganization(googleUserId);
        await CreateIssueAsync(testScope, personalOrganization, new Actor(googleUserId));
        await SetOrganizationTimes(testScope, personalOrganization.Id, createdAt: now.AddDays(-2), setupCompletedAt: now.AddMinutes(1));
        await testScope.Database.Users
            .Where(x => x.Id == googleUserId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.GoogleSubject, "google-subject"));
        await UpdateUser(testScope, googleUserId, createdAt: now.AddDays(-2), lastSeenAt: now.AddHours(-47));

        // Signed up before organization setup existed: the BRD-293 migration marked their organization as set
        // up at its creation, which doesn't count as finishing the setup.
        var backfilledUserId = await testScope.CreateUser();
        var backfilledOrganization = await testScope.InitializePersonalOrganization(backfilledUserId);
        await SetOrganizationTimes(testScope, backfilledOrganization.Id, createdAt: now.AddDays(-90), setupCompletedAt: now.AddDays(-90));
        await UpdateUser(testScope, backfilledUserId, createdAt: now.AddDays(-90), lastSeenAt: null);

        await host.Services.GetRequiredService<BoardsStateMetrics>().RefreshAsync(CancellationToken.None);
        var metrics = await ScrapeAsync(host.CreateClient());

        string Funnel(string cohort, string method, string step) => GaugeValue(
            metrics,
            "boards_funnel_users",
            $"cohort=\"{cohort}\"",
            $"method=\"{method}\"",
            $"step=\"{step}\"");

        foreach (var step in BoardsStateMetrics.FunnelSteps)
        {
            Assert.Equal("1", Funnel("30d", "telegram", step));
            Assert.Equal("0", Funnel("7d", "telegram", step));
            Assert.Equal("0", Funnel("all", "both", step));
        }

        Assert.Equal("3", Funnel("all", "telegram", "registered"));
        Assert.Equal("1", Funnel("all", "telegram", "setup_completed"));
        Assert.Equal("1", Funnel("all", "telegram", "first_issue"));

        Assert.Equal("1", Funnel("7d", "google", "registered"));
        Assert.Equal("1", Funnel("7d", "google", "setup_completed"));
        Assert.Equal("0", Funnel("7d", "google", "first_issue"));
        Assert.Equal("0", Funnel("7d", "google", "org_created"));
        Assert.Equal("0", Funnel("7d", "google", "returned_d1"));
    }

    [Fact]
    public async Task StateMetrics_ShouldCountIssuesCreatedPerWindowAndVia_WhenIssuesAreCreatedAtDifferentTimes()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var apiKey = await testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(organization.Id, userId, "Metrics key", CancellationToken.None);

        await CreateIssueAsync(testScope, organization, new Actor(userId));
        await CreateIssueAsync(testScope, organization, new Actor(userId, apiKey.Id));
        var tenDaysOld = await CreateIssueAsync(testScope, organization, new Actor(userId));
        var deleted = await CreateIssueAsync(testScope, organization, new Actor(userId));
        await testScope.Database.Issues
            .Where(x => x.Id == tenDaysOld)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTime.UtcNow.AddDays(-10)));
        await testScope.Database.Issues
            .Where(x => x.Id == deleted)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeletedAt, DateTime.UtcNow));

        await host.Services.GetRequiredService<BoardsStateMetrics>().RefreshAsync(CancellationToken.None);
        var metrics = await ScrapeAsync(host.CreateClient());

        string Created(string window, string via) => GaugeValue(
            metrics,
            "boards_issues_created_in_window",
            $"window=\"{window}\"",
            $"via=\"{via}\"");

        // The deleted issue still counts: the gauge counts creations.
        Assert.Equal("2", Created("1d", "web"));
        Assert.Equal("1", Created("1d", "mcp"));
        Assert.Equal("3", Created("30d", "web"));
        Assert.Equal("0", Created("30d", "telegram"));
        Assert.Equal("0", Created("30d", "agent"));
    }

    private static Task UpdateUser(WebApiTestHostScope testScope, Guid userId, DateTime createdAt, DateTime? lastSeenAt)
    {
        return testScope.Database.Users
            .Where(x => x.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CreatedAt, createdAt)
                .SetProperty(x => x.LastSeenAt, lastSeenAt));
    }

    private static Task SetOrganizationTimes(
        WebApiTestHostScope testScope,
        long organizationId,
        DateTime createdAt,
        DateTime setupCompletedAt)
    {
        return testScope.Database.Organizations
            .Where(x => x.Id == organizationId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CreatedAt, createdAt)
                .SetProperty(x => x.SetupCompletedAt, setupCompletedAt));
    }

    private static async Task<long> CreateIssueAsync(
        WebApiTestHostScope testScope,
        Organization organization,
        Actor actor,
        long? statusId = null)
    {
        var request = new IssueCreateRequest(
            statusId ?? organization.GetStatus(0, 0, 0).Id,
            DateTime.UtcNow,
            "Metrics issue",
            isTitleSetExplicitly: true);

        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();
        var issueId = await testScope.Services.GetRequiredService<ICoreIssuesService>()
            .Create(actor, request, CancellationToken.None);
        await transaction.CommitAsync();

        return issueId;
    }

    private static Task<string> ScrapeAsync(HttpClient client) => client.GetStringAsync("/_metrics");

    private static IEnumerable<string> SeriesLines(string metrics, string series)
        => metrics.Split('\n').Where(line => line.StartsWith(series + "{"));

    private static string GaugeValue(string metrics, string series, params string[] labels)
    {
        var line = SeriesLines(metrics, series).Single(x => labels.All(x.Contains));

        return line[(line.LastIndexOf(' ') + 1)..];
    }
}
