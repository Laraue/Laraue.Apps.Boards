using System.Net;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class ApiKeysControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private static readonly PaginationData DefaultPagination = new() { Page = 0, PerPage = 10 };

    private readonly Proxy<ApiKeysController> _apiKeysController = host.Controller<ApiKeysController>();

    [Fact]
    public async Task Create_ShouldReturnRawKeyOnce_WhenCalled()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        var response = await _apiKeysController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(new CreateApiKeyRequest { Name = "Claude MCP" }));

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.StartsWith("brdk_", response.RawKey);

        var keys = await _apiKeysController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetAll(new GetApiKeysRequest { Pagination = DefaultPagination }));

        var key = Assert.Single(keys.Data);
        Assert.Equal(response.Id, key.Id);
        Assert.Equal("Claude MCP", key.Name);
        Assert.Equal(response.RawKey[..12], key.KeyPrefix);
        Assert.Null(key.LastUsedAt);
        Assert.Null(key.RevokedAt);
    }

    [Fact]
    public async Task GetAll_ShouldOnlyReturnCallersOwnKeys_WhenOtherMembersHaveKeysToo()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder
                .SetGlobalAccessLevel(x => x.CanRead = true)));

        await _apiKeysController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.Create(new CreateApiKeyRequest { Name = "Owner key" }));

        var memberKeys = await _apiKeysController
            .WithOrganizationAuthorization(organization.Id, memberId)
            .Execute(x => x.GetAll(new GetApiKeysRequest { Pagination = DefaultPagination }));

        Assert.Empty(memberKeys.Data);
    }

    [Fact]
    public async Task Revoke_ShouldPreventKeyFromBeingUsableAgain_WhenKeyIsRevoked()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        var created = await _apiKeysController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(new CreateApiKeyRequest { Name = "Claude MCP" }));

        await _apiKeysController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Revoke(created.Id));

        var keys = await _apiKeysController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetAll(new GetApiKeysRequest { Pagination = DefaultPagination }));

        var key = Assert.Single(keys.Data);
        Assert.NotNull(key.RevokedAt);

        var coreApiKeysService = testScope.Services.GetRequiredService<ICoreApiKeysService>();
        var principal = await coreApiKeysService.ValidateAsync(created.RawKey, CancellationToken.None);
        Assert.Null(principal);
    }

    [Fact]
    public async Task Revoke_ShouldReturnNotFound_WhenKeyBelongsToAnotherUser()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder
                .SetGlobalAccessLevel(x => x.CanRead = true)));

        var created = await _apiKeysController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.Create(new CreateApiKeyRequest { Name = "Owner key" }));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _apiKeysController
            .WithOrganizationAuthorization(organization.Id, memberId)
            .Execute(x => x.Revoke(created.Id)));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }
}
