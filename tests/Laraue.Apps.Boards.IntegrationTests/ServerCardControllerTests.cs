using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.McpHost.Controllers;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class ServerCardControllerTests(McpHostTestHost host) : IClassFixture<McpHostTestHost>
{
    [Fact]
    public async Task Get_ShouldReturnDiscoveryDocument_WhenCalled()
    {
        var card = await host.Controller<ServerCardController>()
            .Execute(c => c.Get());

        Assert.Equal("com.laraue/boards", card!.Name);
        Assert.Equal("https://boards.laraue.com", card.WebsiteUrl);
        Assert.NotEmpty(card.Version);
        Assert.NotEmpty(card.Description);
        Assert.NotEmpty(card.Title);
        Assert.NotEmpty(card.Schema);

        var remote = Assert.Single(card.Remotes);
        Assert.Equal("streamable-http", remote.Type);
        Assert.Equal("http://localhost:5202/mcp", remote.Url); // appsettings.json's ServerCard:PublicMcpUrl
        Assert.Contains("2025-06-18", remote.SupportedProtocolVersions);

        var header = Assert.Single(remote.Headers);
        Assert.Equal("X-Api-Key", header.Name);
        Assert.NotEmpty(header.Description);
        Assert.True(header.IsRequired);
        Assert.True(header.IsSecret);
    }
}
