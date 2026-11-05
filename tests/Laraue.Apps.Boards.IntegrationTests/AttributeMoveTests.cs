using System.Net;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Migrations;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services.AttributeRequests;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// What happens to issues' attribute values when they move (BRD-179): all kept within the organization, also of
/// attributes the new epic or space doesn't use; all removed when they leave the organization.
/// </summary>
[Collection("IntegrationTest")]
public class AttributeMoveTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<IssuesController> _issuesController = host.Controller<IssuesController>();
    private readonly Proxy<MovementController> _movementController = host.Controller<MovementController>();

    [Fact]
    public async Task UpdateStatus_ShouldKeepValues_WhenIssueMovesToEpicNotUsingTheirAttributes()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await SetEpicSettingsAsync(testScope, organization, "Next", ("Type", true), ("Priority", false));

        await UpdateStatusAsync(organization, ownerId, "Next");

        Assert.Equal(["Priority", "Type"], await GetValueNamesAsync(testScope, organization));
    }

    [Fact]
    public async Task McpEditIssueStatus_ShouldKeepValues_WhenIssueMovesToEpicNotUsingTheirAttributes()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await SetEpicSettingsAsync(testScope, organization, "Next", ("Type", false), ("Priority", true));

        await IssueMcpServiceTests.CreateIssueMcpService(testScope).EditIssueStatus(
            IssueMcpServiceTests.AuthDataFor(organization.Id, ownerId),
            IssueKey(organization),
            Epic(organization, "Next").Statuses![0].Id,
            comment: null,
            CancellationToken.None);

        Assert.Equal(["Priority", "Type"], await GetValueNamesAsync(testScope, organization));
    }

    [Fact]
    public async Task MoveEpic_ShouldKeepValues_WhenNewSpaceDoesNotUseTheirAttributes()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        await SetSpaceSettingsAsync(testScope, organization, TargetSpaceKey, ("Type", true), ("Priority", false));

        await MoveEpicAsync(organization, ownerId, organization.Id);

        Assert.Equal(["Priority", "Type"], await GetValueNamesAsync(testScope, organization));
    }

    [Fact]
    public async Task MoveEpic_ShouldRemoveValuesAndOwnSettings_WhenEpicMovesToAnotherOrganization()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var otherOrganization = await testScope.InitializeOrganization(ownerId, org => org
            .AddTextAttribute("Foreign")
            .AddSpace(ownerId, TargetSpaceKey));
        await SetEpicSettingsAsync(testScope, organization, "Sprint", ("Type", true), ("Priority", true));

        await MoveEpicAsync(organization, ownerId, otherOrganization.Id);

        Assert.Empty(await GetValueNamesAsync(testScope, organization));
        var sprintId = Epic(organization, "Sprint").Id;
        Assert.False(await testScope.Database.EpicAttributeSettings.AnyAsync(x => x.EpicId == sprintId));
    }

    [Fact]
    public async Task MoveSpace_ShouldRemoveValuesAndSpaceSettings_WhenSpaceMovesToAnotherOrganization()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var otherOrganization = await testScope.InitializeOrganization(ownerId, org => org.AddTextAttribute("Foreign"));
        await SetSpaceSettingsAsync(testScope, organization, SpaceKey, ("Type", true), ("Priority", true));

        await _movementController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.MoveSpace(SpaceKey, otherOrganization.Id, CancellationToken.None));

        Assert.Empty(await GetValueNamesAsync(testScope, organization));
        var spaceId = Space(organization, SpaceKey).Id;
        Assert.False(await testScope.Database.SpaceAttributeSettings.AnyAsync(x => x.SpaceId == spaceId));
    }

    [Fact]
    public async Task CleanupMigration_ShouldRemoveValuesOfAnotherOrganizationsAttributes_WhenEarlierMoveLeftThem()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var otherOrganization = await testScope.InitializeOrganization(ownerId, org => org
            .AddTextAttribute("Foreign")
            .AddIssueToDefaultStatus(ownerId, issue => issue.WithAttributeValue(0, "Stays")));
        var spaceId = Space(organization, SpaceKey).Id;

        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();
        // How a space moved before BRD-179: only its organization changed.
        await testScope.Database.Spaces
            .Where(x => x.Id == spaceId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.OrganizationId, otherOrganization.Id)
                .SetProperty(x => x.Key, "MVD"));

        await testScope.Database.Database.ExecuteSqlRawAsync(RemoveCrossOrganizationAttributeValues.CleanupSql);

        var movedValues = await GetValueNamesAsync(testScope, organization);
        var foreignAttributeId = otherOrganization.GetAttribute(0).Id;
        var otherValueStays = await testScope.Database.IssueAttributeTextValues
            .AnyAsync(x => x.AttributeId == foreignAttributeId);
        await transaction.RollbackAsync();

        Assert.Empty(movedValues);
        Assert.True(otherValueStays);
    }

    [Fact]
    public async Task Update_ShouldRecordRemovedListValueUnderItsAttributeName_WhenListValueIsCleared()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var request = new UpdateIssueRequest
        {
            Title = "Title",
            AssigneeId = ownerId,
            AttributeValues = [new StringAttributeValue { AttributeId = AttributeId(organization, "Priority"), Value = "High" }],
        };

        await _issuesController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.Update(IssueKey(organization), request, CancellationToken.None));

        var removal = await testScope.Database.OrganizationLogs
            .Where(x => x.EntityId == IssueId(organization) && x.EntityType == LogEntityType.Issue)
            .SelectMany(x => x.Items!)
            .SingleAsync(x => x.PropertyType == PropertyType.Attribute);
        Assert.Equal(("Type", "Bug"), (removal.PropertyName, removal.OldDisplayValue));
    }

    private const string SpaceKey = "MOV";
    private const string TargetSpaceKey = "TGT";

    /// <summary>
    /// Type (list), Priority (text) and Note (text) attributes; space MOV with epics "Sprint" (one issue: Type Bug,
    /// Priority High) and "Next"; an empty space TGT.
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
                        .WithAttributeValue(1, "High")))
                .AddEpic(ownerId, epic => epic.WithName("Next")))
            .AddSpace(ownerId, TargetSpaceKey));
    }

    private static Space Space(Organization organization, string key)
    {
        return organization.Spaces!.Single(x => x.Key == key);
    }

    private static Epic Epic(Organization organization, string name)
    {
        return Space(organization, SpaceKey).Epics!.Single(x => x.Name == name);
    }

    private static Issue Issue(Organization organization)
    {
        return Epic(organization, "Sprint").Statuses!.SelectMany(x => x.Issues ?? []).Single();
    }

    private static long IssueId(Organization organization)
    {
        return Issue(organization).Id;
    }

    private static string IssueKey(Organization organization)
    {
        return $"{SpaceKey}-{Issue(organization).IssueNumber!.Number}";
    }

    private static long AttributeId(Organization organization, string name)
    {
        return organization.Attributes!.Single(x => x.Name == name).Id;
    }

    private static Task SetEpicSettingsAsync(
        WebApiTestHostScope testScope,
        Organization organization,
        string epicName,
        params (string Name, bool IsEnabled)[] settings)
    {
        var epicId = Epic(organization, epicName).Id;
        testScope.Database.EpicAttributeSettings.AddRange(settings
            .Select(x => new EpicAttributeSetting
            {
                EpicId = epicId,
                AttributeId = AttributeId(organization, x.Name),
                IsEnabled = x.IsEnabled,
            }));
        return testScope.Database.SaveChangesAsync();
    }

    private static Task SetSpaceSettingsAsync(
        WebApiTestHostScope testScope,
        Organization organization,
        string spaceKey,
        params (string Name, bool IsEnabled)[] settings)
    {
        var spaceId = Space(organization, spaceKey).Id;
        testScope.Database.SpaceAttributeSettings.AddRange(settings
            .Select(x => new SpaceAttributeSetting
            {
                SpaceId = spaceId,
                AttributeId = AttributeId(organization, x.Name),
                IsEnabled = x.IsEnabled,
            }));
        return testScope.Database.SaveChangesAsync();
    }

    /// <summary>
    /// The names of the attributes the issue has values for, by name.
    /// </summary>
    private static async Task<string[]> GetValueNamesAsync(WebApiTestHostScope testScope, Organization organization)
    {
        var issueId = IssueId(organization);
        var text = await testScope.Database.IssueAttributeTextValues
            .Where(x => x.IssueId == issueId)
            .Select(x => x.Attribute!.Name)
            .ToArrayAsync();
        var list = await testScope.Database.IssueAttributeListValues
            .Where(x => x.IssueId == issueId)
            .Select(x => x.Attribute!.Name)
            .ToArrayAsync();
        return text
            .Concat(list)
            .Order()
            .ToArray();
    }

    private Task UpdateStatusAsync(
        Organization organization,
        Guid userId,
        string epicName)
    {
        var request = new UpdateIssuesStatusRequest
        {
            IssueKeys = [IssueKey(organization)],
            StatusId = Epic(organization, epicName).Statuses![0].Id,
        };
        return _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.UpdateStatus(request, CancellationToken.None));
    }

    private Task MoveEpicAsync(
        Organization organization,
        Guid userId,
        long newOrganizationId)
    {
        var request = new MoveEpicRequest
        {
            SourceEpicId = Epic(organization, "Sprint").Id,
            NewSpaceKey = TargetSpaceKey,
            NewOrganizationId = newOrganizationId,
        };
        return _movementController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.MoveEpic(request, CancellationToken.None));
    }
}
