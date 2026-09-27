using System.Text.Json;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.McpHost;
using Laraue.Apps.Boards.McpHost.Controllers;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class ServerCardControllerTests(McpHostTestHost host) : IClassFixture<McpHostTestHost>
{
    [Fact]
    public async Task Get_ShouldServeServerJsonValues_WhenCalled()
    {
        var serverJson = await ReadRepositoryServerJsonAsync();

        var card = await host.Controller<ServerCardController>()
            .Execute(c => c.Get());

        Assert.Equal(serverJson.Name, card!.Name);
        Assert.Equal(serverJson.Version, card.Version);
        Assert.Equal(serverJson.Description, card.Description);
        Assert.Equal(serverJson.Title, card.Title);
        Assert.Equal(serverJson.WebsiteUrl, card.WebsiteUrl);
        Assert.NotEmpty(card.Schema);

        var remote = Assert.Single(card.Remotes);
        var serverJsonRemote = Assert.Single(serverJson.Remotes);
        Assert.Equal(serverJsonRemote.Type, remote.Type);
        Assert.Equal("http://localhost:5202/mcp", remote.Url); // appsettings.json's ServerCard:PublicMcpUrl
        Assert.Contains("2025-11-25", remote.SupportedProtocolVersions);
        Assert.Equal(serverJsonRemote.Headers, remote.Headers);

        var header = Assert.Single(remote.Headers);
        Assert.Equal("X-Api-Key", header.Name);
        Assert.True(header.IsRequired);
        Assert.True(header.IsSecret);
    }

    /// <summary>
    /// The source file, not the embedded copy - so the test fails if the embedding ever stops picking up
    /// the file that gets published to the registry.
    /// </summary>
    private static async Task<ServerManifest> ReadRepositoryServerJsonAsync()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Laraue.Apps.Boards.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "src", "Laraue.Apps.Boards.McpHost", "server.json");
        await using var stream = File.OpenRead(path);

        return (await JsonSerializer.DeserializeAsync<ServerManifest>(stream, JsonSerializerOptions.Web))!;
    }
}
