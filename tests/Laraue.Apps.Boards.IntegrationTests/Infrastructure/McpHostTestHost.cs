using Laraue.Apps.Boards.McpHost;
using Laraue.Core.Testing.Http;
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
}
