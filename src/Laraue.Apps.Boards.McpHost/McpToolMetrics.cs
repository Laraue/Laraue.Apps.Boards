using System.Diagnostics.Metrics;

namespace Laraue.Apps.Boards.McpHost;

/// <summary>
/// McpHost's own metrics, on top of the MCP SDK's (<see cref="SdkMeterName"/>). The SDK times every
/// request and marks tool errors (<c>error_type="tool_error"</c>), but tags calls only as
/// <c>mcp_method_name="tools/call"</c> - not which tool. <see cref="RecordToolCall"/> adds that: one
/// duration per tool call, tagged with the tool and its outcome, so call counts, latency and errors are
/// visible per tool.
/// </summary>
public sealed class McpToolMetrics
{
    public const string MeterName = "Laraue.Apps.Boards.McpHost";

    /// <summary>The MCP SDK's meter: session duration and per-request operation duration.</summary>
    public const string SdkMeterName = "Experimental.ModelContextProtocol";

    /// <summary><see cref="RecordToolCall"/>'s status for a call that succeeded.</summary>
    public const string StatusOk = "ok";

    /// <summary>
    /// <see cref="RecordToolCall"/>'s status for a call that failed with an exception other than our
    /// expected <c>HttpException</c>s - a bug, the SDK's generic error path.
    /// </summary>
    public const string StatusUnhandled = "unhandled";

    private readonly Histogram<double> _toolDuration;

    public McpToolMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _toolDuration = meter.CreateHistogram<double>(
            "boards.mcp.tool.duration",
            unit: "s",
            description: "Duration of MCP tool calls, by tool and status (ok, an HTTP status code for an expected error, or unhandled).");
    }

    public void RecordToolCall(string? toolName, string status, TimeSpan duration)
    {
        _toolDuration.Record(
            duration.TotalSeconds,
            new KeyValuePair<string, object?>("tool", toolName ?? "unknown"),
            new KeyValuePair<string, object?>("status", status));
    }
}
