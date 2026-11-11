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
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.WebApiServices.Metrics;

/// <summary>
/// Gauges of Boards' state, read from the database: how many issues, epics, organizations and retros there are now,
/// how many people used Boards in the last day, week and month, and how far the people who signed up got
/// (<c>boards.funnel.users</c>, BRD-348). They answer "what is true now" and survive a
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

    /// <summary>
    /// Sign-up windows of the funnel; null is every user ever.
    /// </summary>
    private static readonly (string Label, TimeSpan? Window)[] FunnelCohorts =
    [
        ("7d", TimeSpan.FromDays(7)),
        ("30d", TimeSpan.FromDays(30)),
        ("all", null),
    ];

    public const string MethodTelegram = "telegram";
    public const string MethodGoogle = "google";
    public const string MethodBoth = "both";

    private static readonly string[] FunnelMethods = [MethodTelegram, MethodGoogle, MethodBoth];

    /// <summary>
    /// Funnel steps in the order a user usually reaches them. Each is counted on its own: a user who created an
    /// organization without finishing its setup counts in <c>org_created</c> but not in <c>setup_completed</c>.
    /// </summary>
    public static readonly string[] FunnelSteps =
    [
        "registered",
        "setup_completed",
        "first_issue",
        "org_created",
        "teammate_joined",
        "returned_d1",
        "returned_w1",
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<BoardsStateMetrics> _logger;
    private readonly Guid[] _internalGlobalUserIds;

    private volatile Snapshot _snapshot = Snapshot.Empty;

    public BoardsStateMetrics(
        IMeterFactory meterFactory,
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        ILogger<BoardsStateMetrics> logger,
        IOptions<BoardsMetricsOptions> options)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _internalGlobalUserIds = options.Value.GetInternalGlobalUserIds();

        var meter = meterFactory.Create(BoardsMetrics.MeterName);

        meter.CreateObservableGauge(
            "boards.issues",
            () => Measurements(_snapshot.Issues, "state"),
            description: "Issues now, by state: active (not completed) or completed. Deleted issues are not counted, see boards_issues_soft_deleted; the total is the sum.");

        meter.CreateObservableGauge(
            "boards.issues.soft_deleted",
            () => _snapshot.DeletedIssues,
            description: "Soft-deleted issues now, including those deleted with their space, epic or organization. boards_issues plus this is every issue ever created.");

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
            description: "Distinct users seen (a web request, a Telegram update or an MCP call) in the last day, 7 days and 30 days. Seen times are written at most once an hour. Internal users (BoardsMetrics:InternalGlobalUserIds) are left out.");

        meter.CreateObservableGauge(
            "boards.issues.created_in_window",
            () => _snapshot.IssuesCreatedInWindow.Select(x => new Measurement<long>(
                x.Count,
                new KeyValuePair<string, object?>("window", x.Window),
                new KeyValuePair<string, object?>("via", x.Via))),
            description: "Issues created in the last day, 7 days and 30 days, by how they were created (web, telegram, mcp, agent). Deleted issues count too: it counts creations. Read from the database, so unlike boards_issues_created_total it survives a restart. Issues of internal users (BoardsMetrics:InternalGlobalUserIds), their API keys included, are left out.");

        meter.CreateObservableGauge(
            "boards.bot_messages.in_window",
            () => _snapshot.BotMessagesInWindow.Select(x => new Measurement<long>(
                x.Count,
                new KeyValuePair<string, object?>("window", x.Window),
                new KeyValuePair<string, object?>("kind", x.Kind),
                new KeyValuePair<string, object?>("outcome", x.Outcome))),
            description: "Messages the bot sent users on its own (kind: welcome) in the last day, 7 days and 30 days, by outcome: failed (Telegram refused, e.g. the user didn't allow the bot to write), card_within_24h (the user created a card from Telegram within 24 hours), no_card (none yet - a message younger than a day may still get one). Internal users (BoardsMetrics:InternalGlobalUserIds) are left out.");

        meter.CreateObservableGauge(
            "boards.funnel.users",
            () => _snapshot.Funnel.Select(x => new Measurement<long>(
                x.Count,
                new KeyValuePair<string, object?>("cohort", x.Cohort),
                new KeyValuePair<string, object?>("method", x.Method),
                new KeyValuePair<string, object?>("step", x.Step))),
            description: "Users who signed up in the cohort window (7d, 30d, all) and reached each step, by sign-in method (telegram, google, both). Steps: registered, setup_completed (finished the setup of an organization they own), first_issue (created an issue, not counting the setup's demo data), org_created (created a team organization), teammate_joined (a team organization they own has another member), returned_d1 and returned_w1 (seen at least 1 and 7 days after signing up). Internal users (BoardsMetrics:InternalGlobalUserIds) are left out.");
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

        var deletedIssues = await context.Issues
            .Where(x => x.DeletedAt != null)
            .LongCountAsync(cancellationToken);

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

        // The user metrics below leave out the team's own accounts (BoardsMetricsOptions.InternalGlobalUserIds).
        var internalUserIds = _internalGlobalUserIds;

        var activeUsers = new Dictionary<string, long>();
        foreach (var (label, window) in ActiveUserWindows)
        {
            var since = now - window;
            activeUsers[label] = await context.ActiveUsers()
                .Where(x => !internalUserIds.Contains(x.GlobalUserId))
                .Where(x => x.LastSeenAt >= since)
                .LongCountAsync(cancellationToken);
        }

        var issuesCreatedInWindow = new List<IssuesCreatedCount>();
        foreach (var (label, window) in ActiveUserWindows)
        {
            var since = now - window;
            var counts = await context.Issues
                .Where(x => !internalUserIds.Contains(x.Owner!.GlobalUserId))
                .Where(x => x.CreatedAt >= since)
                .GroupBy(x => x.CreatedVia)
                .Select(x => new { Via = x.Key, Count = x.LongCount() })
                .ToListAsync(cancellationToken);

            issuesCreatedInWindow.AddRange(Enum.GetValues<IssueCreatedVia>().Select(via => new IssuesCreatedCount(
                label,
                via.ToString().ToLowerInvariant(),
                counts.SingleOrDefault(c => c.Via == via)?.Count ?? 0)));
        }

        var botMessagesInWindow = await CountBotMessages(context, now, internalUserIds, cancellationToken);

        var funnel = CountFunnel(await GetFunnelUsers(context, internalUserIds, cancellationToken), now);

        _snapshot = new Snapshot(
            deletedIssues,
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
            activeUsers,
            issuesCreatedInWindow,
            botMessagesInWindow,
            funnel);
    }

    public const string OutcomeFailed = "failed";
    public const string OutcomeCardWithin24H = "card_within_24h";
    public const string OutcomeNoCard = "no_card";

    private static async Task<List<BotMessagesCount>> CountBotMessages(
        DatabaseContext context,
        DateTime now,
        Guid[] internalUserIds,
        CancellationToken cancellationToken)
    {
        var since = now - ActiveUserWindows.Max(x => x.Window);

        var messages = await context.UserBotMessages
            .Where(m => !internalUserIds.Contains(m.User!.GlobalUserId))
            .Where(m => m.SentAt >= since)
            .Select(m => new
            {
                m.Kind,
                m.SentAt,
                Outcome = m.Status == BotMessageStatus.Failed
                    ? OutcomeFailed
                    : context.Issues.Any(i => i.OwnerId == m.UserId
                        && i.CreatedVia == IssueCreatedVia.Telegram
                        && i.CreatedAt >= m.SentAt
                        && i.CreatedAt < m.SentAt.AddDays(1))
                        ? OutcomeCardWithin24H
                        : OutcomeNoCard,
            })
            .ToListAsync(cancellationToken);

        var counts = new List<BotMessagesCount>();
        foreach (var (window, length) in ActiveUserWindows)
        {
            foreach (var kind in Enum.GetValues<BotMessageKind>())
            {
                foreach (var outcome in new[] { OutcomeFailed, OutcomeCardWithin24H, OutcomeNoCard })
                {
                    counts.Add(new BotMessagesCount(
                        window,
                        kind.ToString().ToLowerInvariant(),
                        outcome,
                        messages.LongCount(m => m.Kind == kind && m.Outcome == outcome && m.SentAt >= now - length)));
                }
            }
        }

        return counts;
    }

    /// <summary>
    /// One row per user with the steps they reached. A row per user rather than a count per step and cohort: the
    /// cohorts and methods are then split in memory from a single query.
    /// </summary>
    private static Task<List<FunnelUser>> GetFunnelUsers(
        DatabaseContext context,
        Guid[] internalUserIds,
        CancellationToken cancellationToken)
    {
        return context.ActiveUsers()
            .Where(u => !internalUserIds.Contains(u.GlobalUserId))
            .Select(u => new FunnelUser(
                u.CreatedAt,
                u.TelegramId != null,
                u.GoogleSubject != null,
                // The BRD-293 migration marked every organization that existed then as set up at its creation
                // (setup_completed_at = created_at); only a later completion went through the setup window.
                context.Organizations
                    .Any(o => o.OwnerId == u.Id && o.SetupCompletedAt != null && o.SetupCompletedAt > o.CreatedAt),
                // The setup window adds its demo issues through the web API right before it completes, so web
                // issues created up to the completion are the demo's. Telegram and MCP issues always count.
                context.Issues
                    .Any(i => i.OwnerId == u.Id
                        && (i.CreatedVia != IssueCreatedVia.Web
                            || i.Status!.Epic!.Space!.Organization!.SetupCompletedAt == null
                            || i.CreatedAt > i.Status!.Epic!.Space!.Organization!.SetupCompletedAt)),
                context.Organizations
                    .Any(o => o.OwnerId == u.Id && o.Type == OrganizationType.Organization),
                context.ActiveOrganizations()
                    .Any(o => o.OwnerId == u.Id
                        && o.Type == OrganizationType.Organization
                        && context.ActiveOrganizationUsers().Count(m => m.OrganizationId == o.Id) > 1),
                u.LastSeenAt != null && u.LastSeenAt >= u.CreatedAt.AddDays(1),
                u.LastSeenAt != null && u.LastSeenAt >= u.CreatedAt.AddDays(7)))
            .ToListAsync(cancellationToken);
    }

    private static List<FunnelCount> CountFunnel(IReadOnlyList<FunnelUser> users, DateTime now)
    {
        var counts = new List<FunnelCount>();

        foreach (var (cohort, window) in FunnelCohorts)
        {
            var since = window is null ? DateTime.MinValue : now - window.Value;

            foreach (var method in FunnelMethods)
            {
                var inCohort = users
                    .Where(x => x.CreatedAt >= since && x.Method == method)
                    .ToList();

                foreach (var step in FunnelSteps)
                {
                    counts.Add(new FunnelCount(cohort, method, step, inCohort.LongCount(x => x.HasReached(step))));
                }
            }
        }

        return counts;
    }

    private sealed record FunnelUser(
        DateTime CreatedAt,
        bool HasTelegram,
        bool HasGoogle,
        bool SetupCompleted,
        bool FirstIssue,
        bool OrgCreated,
        bool TeammateJoined,
        bool ReturnedD1,
        bool ReturnedW1)
    {
        public string Method => (HasTelegram, HasGoogle) switch
        {
            (true, true) => MethodBoth,
            (false, true) => MethodGoogle,
            _ => MethodTelegram,
        };

        public bool HasReached(string step) => step switch
        {
            "registered" => true,
            "setup_completed" => SetupCompleted,
            "first_issue" => FirstIssue,
            "org_created" => OrgCreated,
            "teammate_joined" => TeammateJoined,
            "returned_d1" => ReturnedD1,
            "returned_w1" => ReturnedW1,
            _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
        };
    }

    private sealed record FunnelCount(string Cohort, string Method, string Step, long Count);

    private sealed record IssuesCreatedCount(string Window, string Via, long Count);

    private sealed record BotMessagesCount(string Window, string Kind, string Outcome, long Count);

    private sealed record Snapshot(
        long DeletedIssues,
        IReadOnlyDictionary<string, long> Issues,
        IReadOnlyDictionary<string, long> Epics,
        IReadOnlyDictionary<string, long> Organizations,
        IReadOnlyDictionary<string, long> Retros,
        IReadOnlyDictionary<string, long> ActiveUsers,
        IReadOnlyList<IssuesCreatedCount> IssuesCreatedInWindow,
        IReadOnlyList<BotMessagesCount> BotMessagesInWindow,
        IReadOnlyList<FunnelCount> Funnel)
    {
        public static readonly Snapshot Empty = new(
            0,
            new Dictionary<string, long>(),
            new Dictionary<string, long>(),
            new Dictionary<string, long>(),
            new Dictionary<string, long>(),
            new Dictionary<string, long>(),
            [],
            [],
            []);
    }
}
