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
/// Which attributes a space or an epic uses and shows on cards (BRD-179): the organization's defaults, or a space's
/// or an epic's own settings, saved with the rest of its settings.
/// </summary>
[Collection("IntegrationTest")]
public class AttributeSettingsSaveTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
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
    public async Task UpdateSpace_ShouldSaveOwnSettingsAndKeepThem_WhenOrganizationDefaultsChangeLater()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);

        await UpdateSpaceAsync(
            organization.Id,
            ownerId,
            Own(Setting(organization, "Type", true, false), Setting(organization, "Priority", true, true)));
        await SetOrganizationDefaultsAsync(testScope, organization, "Type", isEnabled: false, showOnCard: false);

        var settings = await GetSpaceSettingsAsync(organization.Id, ownerId);
        Assert.Equal(AttributeSettingsSource.Space, settings.Source);
        Assert.Equal(
            [("Type", true, false), ("Priority", true, true)],
            settings.Attributes.Select(x => (x.Name, x.IsEnabled, x.ShowOnCard)));
    }

    [Fact]
    public async Task UpdateSpace_ShouldTurnOffAttributesLeftOut_WhenSavingOwnSettings()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);

        await UpdateSpaceAsync(organization.Id, ownerId, Own(Setting(organization, "Type", true, true)));

        var settings = await GetSpaceSettingsAsync(organization.Id, ownerId);
        Assert.Equal(
            [("Type", true, true), ("Priority", false, false)],
            settings.Attributes.Select(x => (x.Name, x.IsEnabled, x.ShowOnCard)));
    }

    [Fact]
    public async Task UpdateSpace_ShouldReturnBadRequestAndChangeNothing_WhenAttributeIsFromAnotherOrganization()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var otherOrganization = await testScope.InitializeOrganization(ownerId, org => org.AddTextAttribute("Foreign"));
        var foreign = new AttributeSettingInput
        {
            AttributeId = otherOrganization.GetAttribute(0).Id,
            IsEnabled = true,
            ShowOnCard = true,
        };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => UpdateSpaceAsync(
            organization.Id,
            ownerId,
            Own(Setting(organization, "Type", false, false), foreign),
            name: "Renamed space"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(AttributeSettingsSource.Organization, (await GetSpaceSettingsAsync(organization.Id, ownerId)).Source);
        var spaceId = Space(organization).Id;
        var space = await testScope.Database.Spaces.AsNoTracking().SingleAsync(x => x.Id == spaceId);
        Assert.NotEqual("Renamed space", space.Name);
    }

    [Fact]
    public async Task UpdateSpace_ShouldReturnBadRequest_WhenAttributeIsListedTwice()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => UpdateSpaceAsync(
            organization.Id,
            ownerId,
            Own(Setting(organization, "Type", true, true), Setting(organization, "Type", false, false))));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateSpace_ShouldFollowOrganizationAgain_WhenOwnSettingsAreDropped()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await UpdateSpaceAsync(organization.Id, ownerId, Own());

        await UpdateSpaceAsync(organization.Id, ownerId, Inherited());

        var settings = await GetSpaceSettingsAsync(organization.Id, ownerId);
        Assert.Equal(AttributeSettingsSource.Organization, settings.Source);
        Assert.All(settings.Attributes, x => Assert.True(x.IsEnabled));
    }

    [Fact]
    public async Task UpdateSpace_ShouldKeepOwnSettings_WhenAttributeSettingsAreNotSent()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await UpdateSpaceAsync(organization.Id, ownerId, Own(Setting(organization, "Type", true, true)));

        await UpdateSpaceAsync(organization.Id, ownerId, attributeSettings: null);

        Assert.Equal(AttributeSettingsSource.Space, (await GetSpaceSettingsAsync(organization.Id, ownerId)).Source);
    }

    [Fact]
    public async Task GetSpaceSettings_ShouldShowNewAttributeAsOff_WhenItWasAddedAfterSpaceSavedOwnSettings()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await UpdateSpaceAsync(
            organization.Id,
            ownerId,
            Own(Setting(organization, "Type", true, true), Setting(organization, "Priority", true, true)));

        await _adminController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.CreateAttribute(
                new CreateAttributeRequest { Name = "Story points", Color = "#000000", Type = AttributeType.Integer },
                CancellationToken.None));

        var storyPoints = (await GetSpaceSettingsAsync(organization.Id, ownerId)).Attributes.Last();
        Assert.Equal(("Story points", false, false), (storyPoints.Name, storyPoints.IsEnabled, storyPoints.ShowOnCard));
    }

    [Fact]
    public async Task GetSpaceSettings_ShouldNotListDeletedAttribute_WhenSpaceHadOwnSettingsForIt()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await UpdateSpaceAsync(organization.Id, ownerId, Own(Setting(organization, "Type", true, true)));
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
        await UpdateSpaceAsync(organization.Id, ownerId, Own(Setting(organization, "Type", true, true)));

        var settings = await GetEpicSettingsAsync(organization.Id, ownerId, SprintId(organization));

        Assert.Equal(AttributeSettingsSource.Space, settings.Source);
        Assert.Equal([("Type", true), ("Priority", false)], settings.Attributes.Select(x => (x.Name, x.IsEnabled)));
    }

    [Fact]
    public async Task UpdateEpic_ShouldSaveOwnSettingsAndKeepThem_WhenSpaceSettingsChangeLater()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var sprintId = SprintId(organization);

        await UpdateEpicAsync(organization.Id, ownerId, sprintId, Own(Setting(organization, "Priority", true, true)));
        await UpdateSpaceAsync(organization.Id, ownerId, Own());

        var settings = await GetEpicSettingsAsync(organization.Id, ownerId, sprintId);
        Assert.Equal(AttributeSettingsSource.Epic, settings.Source);
        Assert.Equal([("Type", false), ("Priority", true)], settings.Attributes.Select(x => (x.Name, x.IsEnabled)));
        var spaceSettings = await GetSpaceSettingsAsync(organization.Id, ownerId);
        Assert.All(spaceSettings.Attributes, x => Assert.False(x.IsEnabled));
    }

    [Fact]
    public async Task UpdateEpic_ShouldFollowSpaceAgain_WhenOwnSettingsAreDropped()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var sprintId = SprintId(organization);
        await UpdateEpicAsync(organization.Id, ownerId, sprintId, Own());

        await UpdateEpicAsync(organization.Id, ownerId, sprintId, Inherited());

        Assert.Equal(
            AttributeSettingsSource.Organization,
            (await GetEpicSettingsAsync(organization.Id, ownerId, sprintId)).Source);
        Assert.False(await testScope.Database.EpicAttributeSettings.AnyAsync(x => x.EpicId == sprintId));
    }

    [Fact]
    public async Task UpdateEpic_ShouldReturnBadRequestAndChangeNothing_WhenBacklogGetsOwnSettings()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var backlogId = Space(organization).Epics!.Single(x => x.IsDefault).Id;

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => UpdateEpicAsync(organization.Id, ownerId, backlogId, Own(), name: "Renamed backlog"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.False(await testScope.Database.EpicAttributeSettings.AnyAsync(x => x.EpicId == backlogId));
        var backlog = await testScope.Database.Epics.AsNoTracking().SingleAsync(x => x.Id == backlogId);
        Assert.NotEqual("Renamed backlog", backlog.Name);
    }

    [Fact]
    public async Task GetEpicSettings_ShouldIncludeSpaceSettingsAsInherited_WhenEpicHasOwnSettings()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var sprintId = SprintId(organization);
        await UpdateSpaceAsync(organization.Id, ownerId, Own(Setting(organization, "Type", true, true)));
        await UpdateEpicAsync(organization.Id, ownerId, sprintId, Own());

        var settings = await GetEpicSettingsAsync(organization.Id, ownerId, sprintId);

        Assert.Equal(AttributeSettingsSource.Epic, settings.Source);
        Assert.Equal(AttributeSettingsSource.Space, settings.Inherited.Source);
        Assert.Equal([("Type", true), ("Priority", false)], settings.Inherited.Attributes.Select(x => (x.Name, x.IsEnabled)));
    }

    [Fact]
    public async Task GetEpicSettings_ShouldFollowSpace_WhenEpicIsBacklog()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var backlogId = Space(organization).Epics!.Single(x => x.IsDefault).Id;
        await UpdateSpaceAsync(organization.Id, ownerId, Own());

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

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => GetSpaceSettingsAsync(organization.Id, strangerId));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateSpace_ShouldReturnForbiddenAndChangeNothing_WhenCallerCanOnlyReadSpace()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var readerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId, org => org
            .AddUser(readerId, builder => builder
                .SetGlobalAccessLevel(x => x.CanRead = true)));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => UpdateSpaceAsync(organization.Id, readerId, Own()));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Equal(AttributeSettingsSource.Organization, (await GetSpaceSettingsAsync(organization.Id, readerId)).Source);
    }

    [Fact]
    public async Task UpdateEpic_ShouldReturnForbidden_WhenCallerCanUpdateIssuesButNotEpics()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var editorId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId, org => org
            .AddUser(editorId, builder => builder
                .SetGlobalAccessLevel(x => x.CanUpdateIssues = true)));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => UpdateEpicAsync(organization.Id, editorId, SprintId(organization), Own()));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateEpic_ShouldSaveOwnSettings_WhenCallerCanUpdateEpics()
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
        var sprintId = SprintId(organization);

        await UpdateEpicAsync(organization.Id, editorId, sprintId, Own());

        Assert.Equal(AttributeSettingsSource.Epic, (await GetEpicSettingsAsync(organization.Id, editorId, sprintId)).Source);
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

    private static AttributeSettingInput Setting(Organization organization, string name, bool isEnabled, bool showOnCard)
    {
        return new AttributeSettingInput
        {
            AttributeId = AttributeId(organization, name),
            IsEnabled = isEnabled,
            ShowOnCard = showOnCard,
        };
    }

    private static AttributeSettingsUpdate Own(params AttributeSettingInput[] settings)
    {
        return new AttributeSettingsUpdate { Overridden = true, Settings = settings };
    }

    private static AttributeSettingsUpdate Inherited()
    {
        return new AttributeSettingsUpdate { Overridden = false };
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

    private Task<AttributeSettingsViewDto> GetSpaceSettingsAsync(long organizationId, Guid userId)
    {
        return _spacesController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.GetAttributeSettings(SpaceKey, CancellationToken.None));
    }

    private Task<AttributeSettingsViewDto> GetEpicSettingsAsync(long organizationId, Guid userId, long epicId)
    {
        return _epicsController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.GetAttributeSettings(epicId, CancellationToken.None));
    }

    private Task UpdateSpaceAsync(
        long organizationId,
        Guid userId,
        AttributeSettingsUpdate? attributeSettings,
        string name = "Space name")
    {
        var request = new UpdateSpaceRequest
        {
            Name = name,
            Color = "#ffffff",
            NewKey = SpaceKey,
            AttributeSettings = attributeSettings,
        };
        return _spacesController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.Update(SpaceKey, request, CancellationToken.None));
    }

    private Task UpdateEpicAsync(
        long organizationId,
        Guid userId,
        long epicId,
        AttributeSettingsUpdate? attributeSettings,
        string name = "Sprint")
    {
        var request = new UpdateEpicRequest { Name = name, Color = "#121212", AttributeSettings = attributeSettings };
        return _epicsController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.Update(epicId, request, CancellationToken.None));
    }
}
