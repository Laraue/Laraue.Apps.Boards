using System.Diagnostics.Metrics;

namespace Laraue.Apps.Retro.Services;

/// <summary>
/// The retro feature's event counter. It sits on the same meter as the rest of Boards' metrics, but Retro.Services
/// does not reference Boards.Services, so the meter name is repeated here and must stay equal to
/// <c>BoardsMetrics.MeterName</c>.
/// </summary>
public sealed class RetroMetrics
{
    public const string MeterName = "Laraue.Apps.Boards";

    private readonly Counter<long> _retrosStarted;

    public RetroMetrics(IMeterFactory meterFactory)
    {
        _retrosStarted = meterFactory.Create(MeterName).CreateCounter<long>(
            "boards.retros.started",
            description: "Retros started.");

        // Exists from the first scrape, so the first retro is visible to increase(), see BoardsMetrics.
        _retrosStarted.Add(0);
    }

    public void RecordRetroStarted() => _retrosStarted.Add(1);
}
