using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// The Claude connector directory requires every tool to have a title and say whether it only reads
/// (readOnlyHint) or, if it writes, whether it destroys data (destructiveHint). Checked through a real
/// client, so it covers what the SDK actually sends.
/// </summary>
[Collection("IntegrationTest")]
public class McpToolAnnotationsTests(WebApiTestHost webApiHost, McpHostTestHost mcpHost)
    : IClassFixture<WebApiTestHost>, IClassFixture<McpHostTestHost>
{
    [Fact]
    public async Task ListTools_ShouldAnnotateEveryTool()
    {
        using var testScope = webApiHost.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);
        var apiKey = await testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(organization.Id, ownerId, "Test key", CancellationToken.None);
        await using var client = await mcpHost.ConnectMcpClientAsync(apiKey.RawKey);

        var tools = await client.ListToolsAsync();

        Assert.NotEmpty(tools);
        Assert.All(tools, tool =>
        {
            var annotations = tool.ProtocolTool.Annotations;
            Assert.NotNull(annotations);
            Assert.False(string.IsNullOrWhiteSpace(annotations.Title), $"{tool.Name} has no title");
            Assert.True(annotations.ReadOnlyHint is not null, $"{tool.Name} has no readOnlyHint");
            Assert.True(
                annotations.ReadOnlyHint == true || annotations.DestructiveHint is not null,
                $"{tool.Name} writes but has no destructiveHint");
        });
    }

    [Theory]
    [InlineData("delete_issue")]
    [InlineData("delete_comment")]
    [InlineData("delete_issue_link")]
    [InlineData("edit_issue")]
    [InlineData("edit_comment")]
    public async Task ListTools_ShouldMarkToolDestructive_WhenItRemovesOrReplacesData(string toolName)
    {
        using var testScope = webApiHost.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);
        var apiKey = await testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(organization.Id, ownerId, "Test key", CancellationToken.None);
        await using var client = await mcpHost.ConnectMcpClientAsync(apiKey.RawKey);

        var tools = await client.ListToolsAsync();

        Assert.True(tools.Single(t => t.Name == toolName).ProtocolTool.Annotations?.DestructiveHint);
    }
}
