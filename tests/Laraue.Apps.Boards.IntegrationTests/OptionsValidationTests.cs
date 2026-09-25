using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class OptionsValidationTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public void WebApiHost_ShouldFailToStart_WhenRequiredOptionIsMissingInProduction()
    {
        using var misconfigured = WithMissingGoogleClientId(Environments.Production);

        var exception = Assert.Throws<OptionsValidationException>(() => misconfigured.CreateClient());

        Assert.Contains(nameof(Laraue.Apps.Boards.WebApiServices.GoogleAuthOptions.ClientId), exception.Message);
    }

    [Fact]
    public void WebApiHost_ShouldStart_WhenRequiredOptionIsMissingInDevelopment()
    {
        using var misconfigured = WithMissingGoogleClientId(Environments.Development);

        using var client = misconfigured.CreateClient();
    }

    private WebApplicationFactory<Program> WithMissingGoogleClientId(string environment)
    {
        return host.WithWebHostBuilder(builder => builder
            .UseEnvironment(environment)
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["GoogleAuth:ClientId"] = string.Empty })));
    }
}
