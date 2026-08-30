using Laraue.Apps.Retro.WebApiHost;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public class RetroWebApiTestHost : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddJsonFile("appsettings.json", optional: true);
        });

        return base.CreateHost(builder);
    }

    /// <summary>
    /// Retro never mints its own tokens - only Boards' login flow does - so
    /// <paramref name="authServices"/> should be a Boards host's <see cref="IServiceProvider"/>
    /// (for <c>IAuthService</c>) unless the caller doesn't need <c>WithOrganizationAuthorization</c>.
    /// </summary>
    public Proxy<TController> Controller<TController>(IServiceProvider? authServices = null)
        where TController : ControllerBase
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        return new Proxy<TController>(client, authServices ?? Services);
    }

    public WebApiTestHostScope CreateTestScope()
    {
        var scope = Services.CreateScope();

        return new WebApiTestHostScope(scope);
    }
}
