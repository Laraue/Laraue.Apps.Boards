using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Scrapes McpHost's Prometheus endpoint after real MCP calls. Meters are observed process-wide, so another
/// test's host can add to the same series - assert that a series exists, not its exact count.
/// </summary>
[Collection("IntegrationTest")]
public class McpMetricsTests(WebApiTestHost webApiHost, McpHostTestHost mcpHost)
    : IClassFixture<WebApiTestHost>, IClassFixture<McpHostTestHost>
{
    [Fact]
    public async Task Metrics_ShouldExposeToolCallsPerToolAndStatus_WhenToolsAreCalled()
    {
        var metrics = await CallToolsAndScrapeAsync();

        Assert.Contains(SeriesLines(metrics, "boards_mcp_tool_duration_seconds_count"), line =>
            line.Contains("tool=\"list_spaces\"") && line.Contains("status=\"ok\""));
        Assert.Contains(SeriesLines(metrics, "boards_mcp_tool_duration_seconds_count"), line =>
            line.Contains("tool=\"get_issue\"") && line.Contains("status=\"404\""));
    }

    [Fact]
    public async Task Metrics_ShouldExposeMcpSdkOperations_WhenToolsAreCalled()
    {
        var metrics = await CallToolsAndScrapeAsync();

        Assert.Contains(SeriesLines(metrics, "mcp_server_operation_duration_seconds_count"), line =>
            line.Contains("mcp_method_name=\"tools/call\""));
    }

    private async Task<string> CallToolsAndScrapeAsync()
    {
        using var testScope = webApiHost.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);
        var apiKey = await testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(organization.Id, ownerId, "Test key", CancellationToken.None);

        await using (var client = await mcpHost.ConnectMcpClientAsync(apiKey.RawKey))
        {
            await client.CallToolAsync("list_spaces");
            await client.CallToolAsync("get_issue", new Dictionary<string, object?> { ["issueKey"] = "ZZZ-1" });
        }

        return await mcpHost.CreateClient().GetStringAsync("/_metrics");
    }

    private static IEnumerable<string> SeriesLines(string metrics, string series)
    {
        return metrics.Split('\n').Where(line => line.StartsWith(series + "{"));
    }
}
