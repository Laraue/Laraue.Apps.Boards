using System.Net;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services.AttributeRequests;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// How a space's or an epic's attribute settings (BRD-179) shape cards, the issue page, attribute writes and the
/// lists of attributes forms, filters and MCP callers pick from.
/// </summary>
[Collection("IntegrationTest")]
public class AttributeUsageTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<IssuesController> _issuesController = host.Controller<IssuesController>();
    private readonly Proxy<SpacesController> _spacesController = host.Controller<SpacesController>();
    private readonly Proxy<EpicsController> _epicsController = host.Controller<EpicsController>();

    [Fact]
    public async Task Search_ShouldShowOnCardsOnlyAttributesShownOnCards_WhenEpicFollowsOrganization()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var priorityId = AttributeId(organization, "Priority");
        await testScope.Database.Attributes
            .Where(x => x.Id == priorityId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ShowOnCard, false));

        var card = await GetCardAsync(organization.Id, ownerId);

        var attribute = Assert.Single(card.Attributes);
        Assert.Equal(("Type", "Bug"), (attribute.Name, attribute.Value));
    }

    [Fact]
    public async Task Search_ShouldNotShowAttributeSwitchedOffInIssueEpic_WhenIssueHasValueForIt()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await SetEpicSettingsAsync(testScope, organization, ("Type", false), ("Priority", true), ("Note", true));

        var card = await GetCardAsync(organization.Id, ownerId);

        Assert.Equal(["Priority"], card.Attributes.Select(x => x.Name));
    }

    [Fact]
    public async Task Search_ShouldShowCardAttributesInOrganizationOrder_WhenItWasChanged()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var typeId = AttributeId(organization, "Type");
        await testScope.Database.Attributes
            .Where(x => x.Id == typeId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.SortOrder, 10));

        var card = await GetCardAsync(organization.Id, ownerId);

        Assert.Equal(["Priority", "Type"], card.Attributes.Select(x => x.Name));
    }

    [Fact]
    public async Task GetIssue_ShouldListEnabledAttributesAndMarkKeptValuesInactive_WhenEpicSwitchedSomeOff()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await SetEpicSettingsAsync(testScope, organization, ("Type", true), ("Priority", false), ("Note", false));

        var issue = await _issuesController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetIssue(IssueKey(organization), CancellationToken.None));

        Assert.Equal(
            [("Type", false), ("Priority", true)],
            issue!.AttributeValues.Select(x => (x.Name, x.IsInactive)));
        Assert.Equal(["Bug", "Feature"], issue.AttributeValues[0].ListValues.Select(x => x.Name));
    }

    [Fact]
    public async Task Update_ShouldReturnBadRequest_WhenIssueGetsValueForAttributeSwitchedOffInItsEpic()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await SetEpicSettingsAsync(testScope, organization, ("Type", true), ("Priority", true), ("Note", false));
        var request = UpdateRequest(
            ownerId,
            TypeBug(organization),
            Text(organization, "Priority", "High"),
            Text(organization, "Note", "Call John"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _issuesController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.Update(IssueKey(organization), request, CancellationToken.None)));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task Update_ShouldChangeKeptValue_WhenAttributeIsSwitchedOffInIssueEpic()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await SetEpicSettingsAsync(testScope, organization, ("Type", true), ("Priority", false), ("Note", false));
        var request = UpdateRequest(ownerId, TypeBug(organization), Text(organization, "Priority", "Low"));

        await _issuesController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.Update(IssueKey(organization), request, CancellationToken.None));

        var priorityId = AttributeId(organization, "Priority");
        var priority = await testScope.Database.IssueAttributeTextValues
            .AsNoTracking()
            .SingleAsync(x => x.AttributeId == priorityId);
        Assert.Equal("Low", priority.Value);
    }

    [Fact]
    public async Task Create_ShouldReturnBadRequestAndCreateNothing_WhenAttributeIsSwitchedOffInEpic()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await SetEpicSettingsAsync(testScope, organization, ("Type", true), ("Priority", true), ("Note", false));
        var request = new CreateIssueRequest
        {
            Title = "New",
            StatusId = Sprint(organization).Statuses![0].Id,
            AssigneeId = ownerId,
            AttributeValues = [Text(organization, "Note", "Call John")],
        };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _issuesController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.Create(request, CancellationToken.None)));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        var sprintId = Sprint(organization).Id;
        Assert.Equal(1, await testScope.Database.Issues.CountAsync(x => x.Status!.EpicId == sprintId));
    }

    [Fact]
    public async Task GetSpaceAttributes_ShouldListOnlyAttributesEnabledInSpace_WhenSpaceHasOwnSettings()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var spaceId = Space(organization).Id;
        testScope.Database.SpaceAttributeSettings.AddRange(
            new SpaceAttributeSetting { SpaceId = spaceId, AttributeId = AttributeId(organization, "Type"), IsEnabled = true },
            new SpaceAttributeSetting { SpaceId = spaceId, AttributeId = AttributeId(organization, "Priority") },
            new SpaceAttributeSetting { SpaceId = spaceId, AttributeId = AttributeId(organization, "Note"), IsEnabled = true });
        await testScope.Database.SaveChangesAsync();

        var attributes = await _spacesController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetAttributes(SpaceKey, CancellationToken.None));

        Assert.Equal(["Type", "Note"], attributes!.Select(x => x.Name));
        Assert.Equal(["Bug", "Feature"], attributes[0].ListValues.Select(x => x.Name));
    }

    [Fact]
    public async Task GetEpicAttributes_ShouldListOnlyAttributesEnabledInEpic_WhenEpicHasOwnSettings()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await SetEpicSettingsAsync(testScope, organization, ("Type", false), ("Priority", true), ("Note", false));

        var attributes = await _epicsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetAttributes(Sprint(organization).Id, CancellationToken.None));

        Assert.Equal(["Priority"], attributes!.Select(x => x.Name));
    }

    [Fact]
    public async Task McpListAttributes_ShouldListOnlyAttributesUsedInEpic_WhenEpicIdIsGiven()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await SetEpicSettingsAsync(testScope, organization, ("Type", true), ("Priority", false), ("Note", true));
        var mcpService = IssueMcpServiceTests.CreateIssueMcpService(testScope);
        var authData = IssueMcpServiceTests.AuthDataFor(organization.Id, ownerId);

        var backlogId = Space(organization).Epics!.Single(x => x.IsDefault).Id;

        var inEpic = await mcpService.ListAttributes(authData, Sprint(organization).Id, CancellationToken.None);
        // The backlog follows its space, which follows the organization here.
        var inBacklog = await mcpService.ListAttributes(authData, backlogId, CancellationToken.None);

        Assert.Equal(["Type", "Note"], inEpic.Select(x => x.Name));
        Assert.Equal(["Type", "Priority", "Note"], inBacklog.Select(x => x.Name));
    }

    [Fact]
    public async Task McpCreateIssue_ShouldThrowBadRequest_WhenAttributeIsSwitchedOffInEpic()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await SetEpicSettingsAsync(testScope, organization, ("Type", true), ("Priority", true), ("Note", false));

        await Assert.ThrowsAsync<BadRequestException>(() => IssueMcpServiceTests.CreateIssueMcpService(testScope)
            .CreateIssue(
                IssueMcpServiceTests.AuthDataFor(organization.Id, ownerId),
                "Content",
                "Title",
                Sprint(organization).Statuses![0].Id,
                assigneeId: null,
                new Dictionary<long, string> { [AttributeId(organization, "Note")] = "Call John" },
                files: null,
                CancellationToken.None));
    }

    private const string SpaceKey = "USE";

    /// <summary>
    /// Type (list), Priority (text) and Note (text) attributes; a space with a "Sprint" epic holding one issue of
    /// Type Bug and Priority High.
    /// </summary>
    private static Task<Organization> InitializeAsync(WebApiTestHostScope testScope, Guid ownerId)
    {
        return testScope.InitializeOrganization(ownerId, org => org
            .AddListAttribute("Type", ["Bug", "Feature"])
            .AddTextAttribute("Priority")
            .AddTextAttribute("Note")
            .AddSpace(ownerId, SpaceKey, space => space
                .AddEpic(ownerId, epic => epic
                    .WithName("Sprint")
                    .AddIssue(ownerId, 0, issue => issue
                        .WithAttributeValue(0, 0)
                        .WithAttributeValue(1, "High")))));
    }

    private static Space Space(Organization organization)
    {
        return organization.Spaces!.Single(x => x.Key == SpaceKey);
    }

    private static Epic Sprint(Organization organization)
    {
        return Space(organization).Epics!.Single(x => x.Name == "Sprint");
    }

    private static string IssueKey(Organization organization)
    {
        var issue = Sprint(organization).Statuses!.SelectMany(x => x.Issues ?? []).Single();
        return $"{SpaceKey}-{issue.IssueNumber!.Number}";
    }

    private static long AttributeId(Organization organization, string name)
    {
        return organization.Attributes!.Single(x => x.Name == name).Id;
    }

    private static Task SetEpicSettingsAsync(
        WebApiTestHostScope testScope,
        Organization organization,
        params (string Name, bool IsEnabled)[] settings)
    {
        var epicId = Sprint(organization).Id;
        testScope.Database.EpicAttributeSettings.AddRange(settings
            .Select(x => new EpicAttributeSetting
            {
                EpicId = epicId,
                AttributeId = AttributeId(organization, x.Name),
                IsEnabled = x.IsEnabled,
                ShowOnCard = x.IsEnabled,
            }));
        return testScope.Database.SaveChangesAsync();
    }

    private static EnumAttributeValue TypeBug(Organization organization)
    {
        var type = organization.Attributes!.Single(x => x.Name == "Type");
        return new EnumAttributeValue { AttributeId = type.Id, ValueId = type.AttributeListValues![0].Id };
    }

    private static StringAttributeValue Text(Organization organization, string name, string value)
    {
        return new StringAttributeValue { AttributeId = AttributeId(organization, name), Value = value };
    }

    private static UpdateIssueRequest UpdateRequest(Guid assigneeId, params AttributeValue[] values)
    {
        return new UpdateIssueRequest { Title = "Title", AssigneeId = assigneeId, AttributeValues = values };
    }

    private async Task<SearchIssueDto> GetCardAsync(long organizationId, Guid userId)
    {
        var result = await _issuesController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.Search(new SearchRequest { Page = 0, PerPage = 10 }, CancellationToken.None));
        return Assert.Single(result!.Data);
    }
}
