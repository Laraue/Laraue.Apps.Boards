namespace Laraue.Apps.Boards.WebApiServices.Metrics;

/// <summary>
/// Settings of <see cref="BoardsStateMetrics"/> (section <c>BoardsMetrics</c>, WebApiHost only).
/// </summary>
public sealed class BoardsMetricsOptions
{
    /// <summary>
    /// Global user ids (Laraue.Apps.Identity, <c>User.GlobalUserId</c>) of the team's own accounts, comma-separated
    /// (BRD-358). Global, so one list - <c>INTERNAL_GLOBAL_USER_IDS</c> in the server's <c>.env</c> - serves every
    /// service. Their activity, including what their API keys and agents do, is left out of the user metrics (active
    /// users, issues created per window, bot messages, the funnel), which are about real users. Content totals
    /// (issues, epics, organizations) still count everything. Empty: nobody is left out.
    /// </summary>
    public string InternalGlobalUserIds { get; set; } = string.Empty;

    /// <summary>
    /// Sign-up sources (<c>User.SignUpSource</c>, BRD-295) that get their own <c>source</c> label in
    /// <c>boards_funnel_users_by_source</c>. Anyone can put anything in a link, so the label set is fixed here: other
    /// values count as <c>other</c>, no source as <c>direct</c>. The raw value stays in the database.
    /// </summary>
    public string[] KnownSignUpSources { get; set; } = [];

    /// <summary>
    /// <see cref="InternalGlobalUserIds"/> parsed. Throws on a value that isn't a Guid - production checks
    /// <see cref="IsValid"/> on start, so it doesn't get that far.
    /// </summary>
    public Guid[] GetInternalGlobalUserIds()
    {
        return Split().Select(Guid.Parse).ToArray();
    }

    public bool IsValid()
    {
        return Split().All(x => Guid.TryParse(x, out _));
    }

    private string[] Split()
    {
        return InternalGlobalUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
