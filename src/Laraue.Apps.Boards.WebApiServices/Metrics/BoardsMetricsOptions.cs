namespace Laraue.Apps.Boards.WebApiServices.Metrics;

/// <summary>
/// Settings of <see cref="BoardsStateMetrics"/> (section <c>BoardsMetrics</c>, WebApiHost only).
/// </summary>
public sealed class BoardsMetricsOptions
{
    /// <summary>
    /// Boards user ids (<c>User.Id</c>) of the team's own accounts, comma-separated (BRD-358) - one string so that a
    /// single environment variable can carry them (<c>BoardsMetrics__InternalUserIds</c>). Their activity, including
    /// what their API keys and agents do, is left out of the user metrics (active users, issues created per window,
    /// bot messages, the funnel), which are about real users. Content totals (issues, epics, organizations) still
    /// count everything. Empty: nobody is left out.
    /// </summary>
    public string InternalUserIds { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="InternalUserIds"/> parsed; throws on a value that isn't a Guid (production validates it on start).
    /// </summary>
    public Guid[] GetInternalUserIds()
    {
        return InternalUserIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Guid.Parse)
            .ToArray();
    }

    public bool IsValid()
    {
        return InternalUserIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(x => Guid.TryParse(x, out _));
    }
}
