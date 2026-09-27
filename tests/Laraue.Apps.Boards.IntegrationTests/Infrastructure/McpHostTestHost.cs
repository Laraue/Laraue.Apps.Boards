using Laraue.Apps.Boards.McpHost;
using Laraue.Core.Testing.Http;
using ModelContextProtocol.Client;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

/// <summary>
/// Same shape as <see cref="WebApiTestHost"/>/<see cref="RetroWebApiTestHost"/>, pointed at
/// <see cref="Laraue.Apps.Boards.McpHost.Program"/> instead - lets controller tests for that host
/// (e.g. <c>ServerCardController</c>) go through a real HTTP request/MVC pipeline via
/// <see cref="Proxy{TController}"/>, same as every other controller in this test project.
/// </summary>
public class McpHostTestHost : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddJsonFile("appsettings.json", optional: true);
        });

        return base.CreateHost(builder);
    }

    public Proxy<TController> Controller<TController>() where TController : ControllerBase
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        return new Proxy<TController>(client, Services);
    }

    /// <summary>
    /// A real MCP client connected to this host's <c>/mcp</c> endpoint, authenticated with
    /// <paramref name="rawApiKey"/> - for tests that need to see exactly what a client receives.
    /// </summary>
    public Task<McpClient> ConnectMcpClientAsync(string rawApiKey)
    {
        var httpClient = CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, "mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["X-Api-Key"] = rawApiKey },
            },
            httpClient,
            ownsHttpClient: true);

        return McpClient.CreateAsync(transport);
    }
}
