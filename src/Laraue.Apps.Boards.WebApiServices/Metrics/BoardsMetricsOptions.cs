namespace Laraue.Apps.Boards.WebApiServices.Metrics;

/// <summary>
/// Settings of <see cref="BoardsStateMetrics"/> (section <c>BoardsMetrics</c>, WebApiHost only).
/// </summary>
public sealed class BoardsMetricsOptions
{
    /// <summary>
    /// Boards user ids (<c>User.Id</c>) of the team's own accounts (BRD-358). Their activity - including what their
    /// API keys and agents do - is left out of the user metrics (active users, issues created per window, bot
    /// messages, the funnel), which are about real users. Content totals (issues, epics, organizations) still count
    /// everything.
    /// </summary>
    public Guid[] InternalUserIds { get; set; } = [];
}
