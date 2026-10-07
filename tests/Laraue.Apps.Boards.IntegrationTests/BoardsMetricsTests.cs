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

    private static string GaugeValue(string metrics, string series, string label)
    {
        var line = SeriesLines(metrics, series).Single(x => x.Contains(label));

        return line[(line.LastIndexOf(' ') + 1)..];
    }
}
