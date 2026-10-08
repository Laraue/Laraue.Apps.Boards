using System.Diagnostics.Metrics;
using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.Services.Metrics;

/// <summary>
/// Boards' event counters, recorded by whichever host handles the event (web API, Telegram bot, MCP). Every
/// label has a few values at most: never a user, organization or issue id. State that must survive a restart
/// (how many issues, epics, organizations there are now) is a gauge read from the database, see
/// <c>BoardsStateMetrics</c> in WebApiServices. A counter is recorded after the row is saved.
/// </summary>
public sealed class BoardsMetrics
{
    public const string MeterName = "Laraue.Apps.Boards";

    public const string SourceWeb = "web";
    public const string SourceTelegram = "telegram";
    public const string SourceMcp = "mcp";

    private readonly Counter<long> _issuesCreated;
    private readonly Counter<long> _issuesCompleted;
    private readonly Counter<long> _issuesDeleted;
    private readonly Counter<long> _organizationsCreated;

    public BoardsMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _issuesCreated = meter.CreateCounter<long>(
            "boards.issues.created",
            description: "Issues created, by source (web, telegram, mcp).");

        _issuesCompleted = meter.CreateCounter<long>(
            "boards.issues.completed",
            description: "Issues moved into a completed status.");

        _issuesDeleted = meter.CreateCounter<long>(
            "boards.issues.deleted",
            description: "Issues deleted one by one (soft delete). Issues deleted together with their space, epic or organization are not counted here, but are in the boards_issues_soft_deleted gauge.");

        _organizationsCreated = meter.CreateCounter<long>(
            "boards.organizations.created",
            description: "Organizations created, by type (organization, or the personal one made at sign-up).");

        // A counter series only exists after its first event, and rate()/increase() cannot see that first
        // event. Recording a zero for every known label value makes each series exist from the first scrape.
        foreach (var source in new[] { SourceWeb, SourceTelegram, SourceMcp })
        {
            _issuesCreated.Add(0, new KeyValuePair<string, object?>("source", source));
        }

        _issuesCompleted.Add(0);
        _issuesDeleted.Add(0);

        foreach (var type in Enum.GetValues<OrganizationType>())
        {
            _organizationsCreated.Add(0, new KeyValuePair<string, object?>("type", OrganizationTypeLabel(type)));
        }
    }

    /// <summary>
    /// A Telegram message makes it a telegram issue, an API key (what MCP calls use) an mcp one, anything else
    /// is the web app.
    /// </summary>
    public void RecordIssueCreated(long? telegramMessageId, Guid? apiKeyId)
    {
        var source = telegramMessageId is not null
            ? SourceTelegram
            : apiKeyId is not null ? SourceMcp : SourceWeb;

        _issuesCreated.Add(1, new KeyValuePair<string, object?>("source", source));
    }

    public void RecordIssuesCompleted(long count)
    {
        if (count > 0)
        {
            _issuesCompleted.Add(count);
        }
    }

    public void RecordIssueDeleted() => _issuesDeleted.Add(1);

    public void RecordOrganizationCreated(OrganizationType type)
        => _organizationsCreated.Add(1, new KeyValuePair<string, object?>("type", OrganizationTypeLabel(type)));

    public static string OrganizationTypeLabel(OrganizationType type)
        => type == OrganizationType.Personal ? "personal" : "organization";
}
