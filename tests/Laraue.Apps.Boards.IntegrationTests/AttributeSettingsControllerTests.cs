using System.Net;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services.Attributes;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Which attributes a space or an epic uses and shows on cards (BRD-179): the organization's defaults, a space's own
/// settings, an epic's own settings.
/// </summary>
[Collection("IntegrationTest")]
public class AttributeSettingsControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<SpacesController> _spacesController = host.Controller<SpacesController>();
    private readonly Proxy<EpicsController> _epicsController = host.Controller<EpicsController>();
    private readonly Proxy<AdminOrganizationsController> _adminController = host.Controller<AdminOrganizationsController>();

    [Fact]
    public async Task GetSpaceSettings_ShouldReturnOrganizationDefaults_WhenSpaceHasNoOwnSettings()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await SetOrganizationDefaultsAsync(testScope, organization, "Priority", isEnabled: false, showOnCard: false);

        var settings = await GetSpaceSettingsAsync(organization.Id, ownerId);

        Assert.Equal(AttributeSettingsSource.Organization, settings.Source);
        Assert.Equal(
            [("Type", true, true), ("Priority", false, false)],
            settings.Attributes.Select(x => (x.Name, x.IsEnabled, x.ShowOnCard)));
    }

    [Fact]
    public async Task OverrideSpaceSettings_ShouldCopyOrganizationDefaultsAndKeepThem_WhenDefaultsChangeLater()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await SetOrganizationDefaultsAsync(testScope, organization, "Priority", isEnabled: true, showOnCard: false);

        var overridden = await OverrideSpaceSettingsAsync(organization.Id, ownerId);
        await SetOrganizationDefaultsAsync(testScope, organization, "Type", isEnabled: false, showOnCard: false);

        Assert.Equal(AttributeSettingsSource.Space, overridden.Source);
        var settings = await GetSpaceSettingsAsync(organization.Id, ownerId);
        Assert.Equal(AttributeSettingsSource.Space, settings.Source);
        Assert.Equal(
            [("Type", true, true), ("Priority", true, false)],
            settings.Attributes.Select(x => (x.Name, x.IsEnabled, x.ShowOnCard)));
    }

    [Fact]
    public async Task UpdateSpaceSettings_ShouldSaveThemAndTurnOffAttributesLeftOut_WhenSpaceHasOwnSettings()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await OverrideSpaceSettingsAsync(organization.Id, ownerId);

        var settings = await UpdateSpaceSettingsAsync(
            organization.Id,
            ownerId,
            [Setting(AttributeId(organization, "Type"), isEnabled: true, showOnCard: false)]);

        Assert.Equal(
            [("Type", true, false), ("Priority", false, false)],
            settings.Attributes.Select(x => (x.Name, x.IsEnabled, x.ShowOnCard)));
    }

    [Fact]
    public async Task UpdateSpaceSettings_ShouldReturnBadRequest_WhenSpaceFollowsOrganization()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => UpdateSpaceSettingsAsync(
            organization.Id,
            ownerId,
            [Setting(AttributeId(organization, "Type"), isEnabled: false, showOnCard: false)]));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateSpaceSettings_ShouldReturnBadRequestAndChangeNothing_WhenAttributeIsFromAnotherOrganization()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var otherOrganization = await testScope.InitializeOrganization(ownerId, org => org
            .AddTextAttribute("Foreign"));
        await OverrideSpaceSettingsAsync(organization.Id, ownerId);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => UpdateSpaceSettingsAsync(
            organization.Id,
            ownerId,
            [
                Setting(AttributeId(organization, "Type"), isEnabled: false, showOnCard: false),
                Setting(otherOrganization.GetAttribute(0).Id, isEnabled: true, showOnCard: true),
            ]));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        var settings = await GetSpaceSettingsAsync(organization.Id, ownerId);
        Assert.True(settings.Attributes.Single(x => x.Name == "Type").IsEnabled);
    }

    [Fact]
    public async Task UpdateSpaceSettings_ShouldReturnBadRequest_WhenAttributeIsListedTwice()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await OverrideSpaceSettingsAsync(organization.Id, ownerId);
        var typeId = AttributeId(organization, "Type");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => UpdateSpaceSettingsAsync(
            organization.Id,
            ownerId,
            [Setting(typeId, isEnabled: true, showOnCard: true), Setting(typeId, isEnabled: false, showOnCard: false)]));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task ResetSpaceSettings_ShouldFollowOrganizationAgain_WhenSpaceHadOwnSettings()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await OverrideSpaceSettingsAsync(organization.Id, ownerId);
        await UpdateSpaceSettingsAsync(organization.Id, ownerId, []);

        var settings = await _spacesController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.ResetAttributeSettings(SpaceKey, CancellationToken.None));

        Assert.Equal(AttributeSettingsSource.Organization, settings.Source);
        Assert.All(settings.Attributes, x => Assert.True(x.IsEnabled));
    }

    [Fact]
    public async Task GetSpaceSettings_ShouldShowNewAttributeAsOff_WhenItWasAddedAfterSpaceSavedOwnSettings()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await OverrideSpaceSettingsAsync(organization.Id, ownerId);

        await _adminController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.CreateAttribute(
                new CreateAttributeRequest { Name = "Story points", Color = "#000000", Type = AttributeType.Integer },
                CancellationToken.None));

        var settings = await GetSpaceSettingsAsync(organization.Id, ownerId);
        var storyPoints = settings.Attributes.Last();
        Assert.Equal("Story points", storyPoints.Name);
        Assert.False(storyPoints.IsEnabled);
        Assert.False(storyPoints.ShowOnCard);
    }

    [Fact]
    public async Task GetSpaceSettings_ShouldNotListDeletedAttribute_WhenSpaceHadOwnSettingsForIt()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await OverrideSpaceSettingsAsync(organization.Id, ownerId);
        var priorityId = AttributeId(organization, "Priority");

        await _adminController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.DeleteAttribute(priorityId, CancellationToken.None));

        var settings = await GetSpaceSettingsAsync(organization.Id, ownerId);
        Assert.Equal(AttributeSettingsSource.Space, settings.Source);
        Assert.Equal(["Type"], settings.Attributes.Select(x => x.Name));
        Assert.False(await testScope.Database.SpaceAttributeSettings.AnyAsync(x => x.AttributeId == priorityId));
    }

    [Fact]
    public async Task GetEpicSettings_ShouldFollowSpace_WhenEpicHasNoOwnSettings()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await OverrideSpaceSettingsAsync(organization.Id, ownerId);
        await UpdateSpaceSettingsAsync(
            organization.Id,
            ownerId,
            [Setting(AttributeId(organization, "Type"), isEnabled: true, showOnCard: true)]);

        var settings = await GetEpicSettingsAsync(organization.Id, ownerId, SprintId(organization));

        Assert.Equal(AttributeSettingsSource.Space, settings.Source);
        Assert.Equal(
            [("Type", true), ("Priority", false)],
            settings.Attributes.Select(x => (x.Name, x.IsEnabled)));
    }

    [Fact]
    public async Task OverrideEpicSettings_ShouldCopySpaceSettingsAndKeepThem_WhenSpaceSettingsChangeLater()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var sprintId = SprintId(organization);
        await OverrideSpaceSettingsAsync(organization.Id, ownerId);
        await UpdateSpaceSettingsAsync(
            organization.Id,
            ownerId,
            [Setting(AttributeId(organization, "Type"), isEnabled: true, showOnCard: true)]);

        await OverrideEpicSettingsAsync(organization.Id, ownerId, sprintId);
        await UpdateSpaceSettingsAsync(organization.Id, ownerId, []);

        var settings = await GetEpicSettingsAsync(organization.Id, ownerId, sprintId);
        Assert.Equal(AttributeSettingsSource.Epic, settings.Source);
        Assert.Equal(
            [("Type", true), ("Priority", false)],
            settings.Attributes.Select(x => (x.Name, x.IsEnabled)));
    }

    [Fact]
    public async Task UpdateEpicSettings_ShouldNotChangeSpaceSettings_WhenEpicHasOwnSettings()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var sprintId = SprintId(organization);
        await OverrideEpicSettingsAsync(organization.Id, ownerId, sprintId);
        var request = new UpdateEpicAttributeSettingsRequest
        {
            Settings = [Setting(AttributeId(organization, "Priority"), isEnabled: true, showOnCard: true)],
        };

        var epicSettings = await _epicsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.UpdateAttributeSettings(sprintId, request, CancellationToken.None));

        Assert.Equal(
            [("Type", false), ("Priority", true)],
            epicSettings.Attributes.Select(x => (x.Name, x.IsEnabled)));
        var spaceSettings = await GetSpaceSettingsAsync(organization.Id, ownerId);
        Assert.Equal(AttributeSettingsSource.Organization, spaceSettings.Source);
        Assert.All(spaceSettings.Attributes, x => Assert.True(x.IsEnabled));
    }

    [Fact]
    public async Task ResetEpicSettings_ShouldFollowSpaceAgain_WhenEpicHadOwnSettings()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var sprintId = SprintId(organization);
        await OverrideEpicSettingsAsync(organization.Id, ownerId, sprintId);

        var settings = await _epicsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.ResetAttributeSettings(sprintId, CancellationToken.None));

        Assert.Equal(AttributeSettingsSource.Organization, settings.Source);
        Assert.False(await testScope.Database.EpicAttributeSettings.AnyAsync(x => x.EpicId == sprintId));
    }

    [Fact]
    public async Task OverrideEpicSettings_ShouldReturnBadRequest_WhenEpicIsBacklog()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var backlogId = Space(organization).Epics!.Single(x => x.IsDefault).Id;

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => OverrideEpicSettingsAsync(organization.Id, ownerId, backlogId));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.False(await testScope.Database.EpicAttributeSettings.AnyAsync(x => x.EpicId == backlogId));
    }

    [Fact]
    public async Task GetEpicSettings_ShouldFollowSpace_WhenEpicIsBacklog()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var backlogId = Space(organization).Epics!.Single(x => x.IsDefault).Id;
        await OverrideSpaceSettingsAsync(organization.Id, ownerId);

        var settings = await GetEpicSettingsAsync(organization.Id, ownerId, backlogId);

        Assert.Equal(AttributeSettingsSource.Space, settings.Source);
    }

    [Fact]
    public async Task GetSpaceSettings_ShouldReturnNotFound_WhenCallerCannotReadSpace()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var strangerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId, org => org.AddUser(strangerId));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => GetSpaceSettingsAsync(organization.Id, strangerId));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task OverrideSpaceSettings_ShouldReturnForbiddenAndChangeNothing_WhenCallerCanOnlyReadSpace()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var readerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId, org => org
            .AddUser(readerId, builder => builder
                .SetGlobalAccessLevel(x => x.CanRead = true)));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => OverrideSpaceSettingsAsync(organization.Id, readerId));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Equal(
            AttributeSettingsSource.Organization,
            (await GetSpaceSettingsAsync(organization.Id, readerId)).Source);
    }

    [Fact]
    public async Task OverrideEpicSettings_ShouldReturnNotFound_WhenCallerCannotReadSpace()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var strangerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId, org => org.AddUser(strangerId));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => OverrideEpicSettingsAsync(organization.Id, strangerId, SprintId(organization)));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task OverrideEpicSettings_ShouldReturnForbidden_WhenCallerCanUpdateIssuesButNotEpics()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var editorId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId, org => org
            .AddUser(editorId, builder => builder
                .SetGlobalAccessLevel(x => x.CanUpdateIssues = true)));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => OverrideEpicSettingsAsync(organization.Id, editorId, SprintId(organization)));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task OverrideEpicSettings_ShouldSucceed_WhenCallerCanUpdateEpics()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var editorId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId, org => org
            .AddUser(editorId, builder => builder
                .SetGlobalAccessLevel(x =>
                {
                    x.CanRead = true;
                    x.CanUpdateEpics = true;
                })));

        var settings = await OverrideEpicSettingsAsync(organization.Id, editorId, SprintId(organization));

        Assert.Equal(AttributeSettingsSource.Epic, settings.Source);
    }

    private const string SpaceKey = "ATR";

    private static Task<Organization> InitializeAsync(
        WebApiTestHostScope testScope,
        Guid ownerId,
        Action<OrganizationInitializer>? configure = null)
    {
        return testScope.InitializeOrganization(ownerId, org =>
        {
            org
                .AddListAttribute("Type", ["Bug", "Feature"])
                .AddTextAttribute("Priority")
                // The first seeded space gets no backlog; the one under test is the second.
                .AddSpace(ownerId, "ONE")
                .AddSpace(ownerId, SpaceKey, space => space
                    .AddEpic(ownerId, epic => epic.WithName("Sprint")));
            configure?.Invoke(org);
        });
    }

    private static Space Space(Organization organization)
    {
        return organization.Spaces!.Single(x => x.Key == SpaceKey);
    }

    private static long SprintId(Organization organization)
    {
        return Space(organization).Epics!.Single(x => x.Name == "Sprint").Id;
    }

    private static long AttributeId(Organization organization, string name)
    {
        return organization.Attributes!.Single(x => x.Name == name).Id;
    }

    private static AttributeSettingInput Setting(long attributeId, bool isEnabled, bool showOnCard)
    {
        return new AttributeSettingInput { AttributeId = attributeId, IsEnabled = isEnabled, ShowOnCard = showOnCard };
    }

    private static Task SetOrganizationDefaultsAsync(
        WebApiTestHostScope testScope,
        Organization organization,
        string attributeName,
        bool isEnabled,
        bool showOnCard)
    {
        var attributeId = AttributeId(organization, attributeName);
        return testScope.Database.Attributes
            .Where(x => x.Id == attributeId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsEnabled, isEnabled)
                .SetProperty(x => x.ShowOnCard, showOnCard));
    }

    private Task<AttributeSettingsDto> GetSpaceSettingsAsync(long organizationId, Guid userId)
    {
        return _spacesController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.GetAttributeSettings(SpaceKey, CancellationToken.None));
    }

    private Task<AttributeSettingsDto> OverrideSpaceSettingsAsync(long organizationId, Guid userId)
    {
        return _spacesController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.OverrideAttributeSettings(SpaceKey, CancellationToken.None));
    }

    private Task<AttributeSettingsDto> UpdateSpaceSettingsAsync(
        long organizationId,
        Guid userId,
        AttributeSettingInput[] settings)
    {
        return _spacesController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.UpdateAttributeSettings(
                SpaceKey,
                new UpdateSpaceAttributeSettingsRequest { Settings = settings },
                CancellationToken.None));
    }

    private Task<AttributeSettingsDto> GetEpicSettingsAsync(long organizationId, Guid userId, long epicId)
    {
        return _epicsController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.GetAttributeSettings(epicId, CancellationToken.None));
    }

    private Task<AttributeSettingsDto> OverrideEpicSettingsAsync(long organizationId, Guid userId, long epicId)
    {
        return _epicsController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.OverrideAttributeSettings(epicId, CancellationToken.None));
    }
}
