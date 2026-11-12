using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Guards <see cref="TestEnvironment"/>: test hosts must not watch their config files, or the suite runs out of
/// inotify instances on Linux.
/// </summary>
[Collection("IntegrationTest")]
public class TestEnvironmentTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public void TestHost_ShouldNotReloadJsonConfig_WhenFilesChange()
    {
        var configuration = (IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>();

        var jsonSources = configuration.Providers
            .OfType<JsonConfigurationProvider>()
            .Select(x => x.Source)
            .ToList();

        Assert.NotEmpty(jsonSources);
        Assert.All(jsonSources, source => Assert.False(source.ReloadOnChange, source.Path));
    }
}
