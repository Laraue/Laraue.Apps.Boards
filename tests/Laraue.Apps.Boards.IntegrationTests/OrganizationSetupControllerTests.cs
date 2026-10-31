using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

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
        var organization = await testScope.InitializeOrganization(ownerId, o => o.AddSpace(ownerId, "SEC"));

        var result = await _organizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetOrganization());

        Assert.NotNull(result);
        Assert.NotNull(result.Setup);
        Assert.Equal("DEF", result.Setup.Space?.Key);
        Assert.Equal("Default Space", result.Setup.Space?.Name);
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
    public async Task CompleteSetup_ShouldRenameSpaceAndAddNothing_WhenTemplateIsEmpty()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);

        await _organizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.CompleteSetup(new CompleteOrganizationSetupRequest
            {
                Space = new OrganizationSetupSpace { Key = "DEF", Name = " Marketing ", NewKey = "mar" },
                Template = OrganizationSetupTemplate.Empty,
            }));

        var space = await testScope.Database.Spaces.AsNoTracking().SingleAsyncEF(x => x.OrganizationId == organization.Id);
        Assert.Equal("Marketing", space.Name);
        Assert.Equal("MAR", space.Key);

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
    public async Task CompleteSetup_ShouldAddSampleProject_WhenTemplateIsSample()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);

        await _organizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.CompleteSetup(new CompleteOrganizationSetupRequest
            {
                Template = OrganizationSetupTemplate.Sample,
            }));

        var space = await testScope.Database.Spaces
            .AsNoTracking()
            .SingleAsyncEF(x => x.OrganizationId == organization.Id && x.Key == "SMP");
        Assert.Equal(SampleProjectContent.SpaceName, space.Name);
        Assert.Equal(SampleProjectContent.SpaceDescription, space.Description);

        var board = await testScope.Database.Epics
            .AsNoTracking()
            .SingleAsyncEF(x => x.SpaceId == space.Id && !x.IsDefault);
        Assert.Equal(SampleProjectContent.BoardName, board.Name);
        Assert.Equal(EpicStatus.Active, board.Status);

        var issues = await testScope.Database.Issues
            .Where(x => x.Status!.Epic!.SpaceId == space.Id)
            .Select(x => new { x.Id, x.Title, x.OwnerId, x.AssigneeId, StatusName = x.Status!.Name })
            .ToListAsyncEF();
        Assert.Equal(SampleProjectContent.Issues.Select(x => x.Title).Order(), issues.Select(x => x.Title).Order());
        Assert.All(issues, x => Assert.Equal(ownerId, x.AssigneeId));
        Assert.Equal("Done", issues.Single(x => x.Title == "Pick the color palette").StatusName);

        var commentedIssue = issues.Single(x => x.Title == "Write the landing page copy");
        Assert.True(await testScope.Database.IssueComments.AnyAsyncEF(x => x.IssueId == commentedIssue.Id));

        var blockedIssue = issues.Single(x => x.Title == "Write the launch announcement");
        Assert.True(await testScope.Database.IssueLinks.AnyAsyncEF(
            x => x.SourceIssueId == commentedIssue.Id && x.TargetIssueId == blockedIssue.Id));

        // Sample issues aren't the user's: they don't use up the plan's monthly issues.
        Assert.False(await testScope.Database.IssuePeriodCounts.AnyAsyncEF(x => x.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task CompleteSetup_ShouldPickAnotherSampleKey_WhenSampleKeyIsTaken()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, o => o.AddSpace(ownerId, "SMP"));

        await _organizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.CompleteSetup(new CompleteOrganizationSetupRequest
            {
                Template = OrganizationSetupTemplate.Sample,
            }));

        Assert.True(await testScope.Database.Spaces.AnyAsyncEF(
            x => x.OrganizationId == organization.Id && x.Key == "DMO" && x.Name == SampleProjectContent.SpaceName));
    }

    [Fact]
    public async Task CompleteSetup_ShouldFailAndKeepSpace_WhenOrganizationIsAlreadySetUp()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);

        await _organizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.CompleteSetup(new CompleteOrganizationSetupRequest
            {
                Template = OrganizationSetupTemplate.Empty,
            }));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _organizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.CompleteSetup(new CompleteOrganizationSetupRequest
            {
                Space = new OrganizationSetupSpace { Key = "DEF", Name = "Renamed", NewKey = "REN" },
                Template = OrganizationSetupTemplate.Sample,
            })));

        var badRequest = ex.HasInnerException<BadRequestException>();
        Assert.Equal(new[] { "The organization is already set up" }, badRequest.Errors["template"]);

        var spaces = await testScope.Database.Spaces
            .Where(x => x.OrganizationId == organization.Id)
            .Select(x => x.Key)
            .ToListAsyncEF();
        Assert.Equal(["DEF"], spaces);
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
            .Execute(x => x.CompleteSetup(new CompleteOrganizationSetupRequest
            {
                Template = OrganizationSetupTemplate.Sample,
            })));

        ex.HasInnerException<NotFoundException>();

        var setupCompletedAt = await testScope.Database.Organizations
            .Where(x => x.Id == organization.Id)
            .Select(x => x.SetupCompletedAt)
            .SingleAsyncEF();
        Assert.Null(setupCompletedAt);
    }
}
