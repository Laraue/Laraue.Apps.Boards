using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.McpHost;
using Laraue.Apps.Boards.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Calls tools through a real MCP client against McpHost, to check what a client actually receives when
/// a tool fails (see <see cref="HttpExceptionToolFilter"/>). Data is seeded through the Boards
/// <see cref="WebApiTestHost"/>, which shares the test database.
/// </summary>
[Collection("IntegrationTest")]
public class McpToolErrorTests(WebApiTestHost webApiHost, McpHostTestHost mcpHost)
    : IClassFixture<WebApiTestHost>, IClassFixture<McpHostTestHost>
{
    [Fact]
    public async Task CallTool_ShouldReturnNotFoundMessage_WhenIssueDoesNotExist()
    {
        using var testScope = webApiHost.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);
        await using var client = await ConnectAsync(testScope, organization.Id, ownerId);

        var result = await client.CallToolAsync("get_issue", new Dictionary<string, object?> { ["issueKey"] = "ZZZ-1" });

        Assert.True(result.IsError);
        Assert.Equal("NotFound: Issue: ZZZ-1 is not found in organization", GetText(result));
    }

    [Fact]
    public async Task CallTool_ShouldReturnForbiddenMessage_WhenActionIsNotAllowed()
    {
        using var testScope = webApiHost.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder.SetGlobalAccessLevel(x =>
            {
                x.CanRead = true;
                x.CanUpdateIssues = true;
            }))
            .AddIssueToDefaultStatus(ownerId, issue => issue
                .WithContent("Fix the thing")
                .AddComment(ownerId, "Original comment")));
        var commentId = organization.GetIssueData(0, 0, 0, 0).Issue.IssueComments!.Single().Id;
        await using var client = await ConnectAsync(testScope, organization.Id, memberId);

        var result = await client.CallToolAsync("edit_comment", new Dictionary<string, object?>
        {
            ["commentId"] = commentId,
            ["text"] = "Edited comment",
        });

        Assert.True(result.IsError);
        Assert.Equal($"Forbidden: Comment: {commentId} edit is forbidden", GetText(result));
    }

    [Fact]
    public async Task CallTool_ShouldReturnFieldErrors_WhenRequestIsInvalid()
    {
        using var testScope = webApiHost.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Fix the thing")));
        var issueKey = organization.GetIssueData(0, 0, 0, 0).Key;
        await using var client = await ConnectAsync(testScope, organization.Id, ownerId);

        var result = await client.CallToolAsync("edit_issue", new Dictionary<string, object?>
        {
            ["issueKey"] = issueKey,
            ["content"] = "Fix the thing",
            ["attributes"] = new Dictionary<string, string?> { ["999999"] = "value" },
        });

        Assert.True(result.IsError);
        var text = GetText(result);
        Assert.StartsWith("BadRequest: ", text);
        Assert.Contains("- attributes: Attribute: 999999 is not found", text);
    }

    private async Task<McpClient> ConnectAsync(WebApiTestHostScope testScope, long organizationId, Guid userId)
    {
        var apiKey = await testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(organizationId, userId, "Test key", CancellationToken.None);

        var httpClient = mcpHost.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, "mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["X-Api-Key"] = apiKey.RawKey },
            },
            httpClient,
            ownsHttpClient: true);

        return await McpClient.CreateAsync(transport);
    }

    private static string GetText(CallToolResult result)
    {
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }
}
