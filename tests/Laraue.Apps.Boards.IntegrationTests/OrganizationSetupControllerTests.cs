using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class OrganizationSetupControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<OrganizationsController> _organizationsController = host.Controller<OrganizationsController>();

    [Fact]
    public async Task GetOrganization_ShouldReturnSetupWithFirstSpace_WhenOrganizationIsNotSetUp()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, o => o
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithContent("Saved from Telegram"))
            .AddSpace(ownerId, "SEC"));

        var result = await _organizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetOrganization());

        Assert.NotNull(result);
        Assert.NotNull(result.Setup);
        Assert.NotNull(result.Setup.Space);
        Assert.Equal("DEF", result.Setup.Space.Key);
        Assert.Equal("Default Space", result.Setup.Space.Name);
        Assert.Equal(1, result.Setup.Space.IssueCount);
    }

    [Fact]
    public async Task GetOrganization_ShouldReturnNoSetup_WhenUserCannotUpdateOrganization()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, o => o.AddUser(memberId));

        var result = await _organizationsController
            .WithOrganizationAuthorization(organization.Id, memberId)
            .Execute(x => x.GetOrganization());

        Assert.NotNull(result);
        Assert.Null(result.Setup);
    }

    [Fact]
    public async Task CompleteSetup_ShouldHideSetup_WhenCompleted()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);

        await _organizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.CompleteSetup(new CompleteOrganizationSetupRequest { DemoDataAdded = true }));

        var setupCompletedAt = await testScope.Database.Organizations
            .Where(x => x.Id == organization.Id)
            .Select(x => x.SetupCompletedAt)
            .SingleAsyncEF();
        Assert.NotNull(setupCompletedAt);

        var result = await _organizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetOrganization());
        Assert.NotNull(result);
        Assert.Null(result.Setup);
    }

    [Fact]
    public async Task CompleteSetup_ShouldFail_WhenOrganizationIsAlreadySetUp()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);

        await _organizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.CompleteSetup(new CompleteOrganizationSetupRequest()));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _organizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.CompleteSetup(new CompleteOrganizationSetupRequest())));

        var badRequest = ex.HasInnerException<BadRequestException>();
        Assert.Equal(new[] { "The organization is already set up" }, badRequest.Errors["demoDataAdded"]);
    }

    [Fact]
    public async Task CompleteSetup_ShouldFail_WhenUserCannotUpdateOrganization()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, o => o.AddUser(memberId));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _organizationsController
            .WithOrganizationAuthorization(organization.Id, memberId)
            .Execute(x => x.CompleteSetup(new CompleteOrganizationSetupRequest())));

        ex.HasInnerException<NotFoundException>();

        var setupCompletedAt = await testScope.Database.Organizations
            .Where(x => x.Id == organization.Id)
            .Select(x => x.SetupCompletedAt)
            .SingleAsyncEF();
        Assert.Null(setupCompletedAt);
    }
}
