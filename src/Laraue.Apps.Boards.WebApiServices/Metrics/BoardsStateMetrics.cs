using System.Diagnostics.Metrics;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.Metrics;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.Boards.WebApiServices.Metrics;

/// <summary>
/// Gauges of Boards' state, read from the database: how many issues, epics, organizations and retros there are now
/// and how many people worked in the last day, week and month. They answer "what is true now" and survive a
/// restart, which event counters cannot - plot them over time to see the totals change. Registered by
/// <c>WebApiHost</c> only; refreshed in the background, a scrape only reads the last snapshot. With several
/// replicas each publishes the same numbers, so query them with <c>max()</c>.
/// </summary>
public sealed class BoardsStateMetrics : BackgroundService
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private static readonly (string Label, TimeSpan Window)[] ActiveUserWindows =
    [
        ("1d", TimeSpan.FromDays(1)),
        ("7d", TimeSpan.FromDays(7)),
        ("30d", TimeSpan.FromDays(30)),
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<BoardsStateMetrics> _logger;

    private volatile Snapshot _snapshot = Snapshot.Empty;

    public BoardsStateMetrics(
        IMeterFactory meterFactory,
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        ILogger<BoardsStateMetrics> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;

        var meter = meterFactory.Create(BoardsMetrics.MeterName);

        meter.CreateObservableGauge(
            "boards.issues",
            () => Measurements(_snapshot.Issues, "state"),
            description: "Issues now, by state: active (not completed) or completed. Deleted issues are not counted; the total is the sum.");

        meter.CreateObservableGauge(
            "boards.epics",
            () => Measurements(_snapshot.Epics, "status"),
            description: "Epics now, by status (new, active, done). Deleted epics are not counted.");

        meter.CreateObservableGauge(
            "boards.organizations",
            () => Measurements(_snapshot.Organizations, "type"),
            description: "Organizations now, by type: organization, or the personal one every user gets. Deleted ones are not counted.");

        meter.CreateObservableGauge(
            "boards.retros",
            () => Measurements(_snapshot.Retros, "state"),
            description: "Retros ever started and not deleted with their organization, by state: running or finished. The total is the sum.");

        meter.CreateObservableGauge(
            "boards.active_users",
            () => Measurements(_snapshot.ActiveUsers, "window"),
            description: "Distinct users who created, changed or deleted an issue or comment in the last day, 7 days and 30 days.");
    }

    private static IEnumerable<Measurement<long>> Measurements(IReadOnlyDictionary<string, long> values, string label)
        => values.Select(x => new Measurement<long>(x.Value, new KeyValuePair<string, object?>(label, x.Key)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);

        do
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The gauges keep their last values; the next tick tries again.
                _logger.LogWarning(ex, "Refreshing the Boards state metrics failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var now = _dateTimeProvider.UtcNow;

        var issues = await context.ActiveIssues()
            .GroupBy(x => x.Status!.Category == StatusCategory.Completed)
            .Select(x => new { IsCompleted = x.Key, Count = x.LongCount() })
            .ToListAsync(cancellationToken);

        var epics = await context.ActiveEpics()
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.LongCount() })
            .ToListAsync(cancellationToken);

        var organizations = await context.ActiveOrganizations()
            .GroupBy(x => x.Type)
            .Select(x => new { Type = x.Key, Count = x.LongCount() })
            .ToListAsync(cancellationToken);

        var retros = await context.Retros
            .Where(x => x.Organization!.DeletedAt == null)
            .GroupBy(x => x.FinishedAt != null)
            .Select(x => new { IsFinished = x.Key, Count = x.LongCount() })
            .ToListAsync(cancellationToken);

        var activeUsers = new Dictionary<string, long>();
        foreach (var (label, window) in ActiveUserWindows)
        {
            var since = now - window;
            activeUsers[label] = await context.OrganizationLogs
                .Where(x => x.CreatedAt >= since)
                .Select(x => x.OwnerId)
                .Distinct()
                .LongCountAsync(cancellationToken);
        }

        _snapshot = new Snapshot(
            new Dictionary<string, long>
            {
                ["active"] = issues.SingleOrDefault(x => !x.IsCompleted)?.Count ?? 0,
                ["completed"] = issues.SingleOrDefault(x => x.IsCompleted)?.Count ?? 0,
            },
            Enum.GetValues<EpicStatus>().ToDictionary(
                x => x.ToString().ToLowerInvariant(),
                x => epics.SingleOrDefault(e => e.Status == x)?.Count ?? 0),
            Enum.GetValues<OrganizationType>().ToDictionary(
                BoardsMetrics.OrganizationTypeLabel,
                x => organizations.SingleOrDefault(o => o.Type == x)?.Count ?? 0),
            new Dictionary<string, long>
            {
                ["running"] = retros.SingleOrDefault(x => !x.IsFinished)?.Count ?? 0,
                ["finished"] = retros.SingleOrDefault(x => x.IsFinished)?.Count ?? 0,
            },
            activeUsers);
    }

    private sealed record Snapshot(
        IReadOnlyDictionary<string, long> Issues,
        IReadOnlyDictionary<string, long> Epics,
        IReadOnlyDictionary<string, long> Organizations,
        IReadOnlyDictionary<string, long> Retros,
        IReadOnlyDictionary<string, long> ActiveUsers)
    {
        public static readonly Snapshot Empty = new(
            new Dictionary<string, long>(),
            new Dictionary<string, long>(),
            new Dictionary<string, long>(),
            new Dictionary<string, long>(),
            new Dictionary<string, long>());
    }
}
