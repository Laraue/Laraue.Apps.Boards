using System.Net;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// The organization's attribute defaults and order (BRD-179), set through the admin API.
/// </summary>
[Collection("IntegrationTest")]
public class OrganizationAttributesControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<AdminOrganizationsController> _adminController = host.Controller<AdminOrganizationsController>();
    private readonly Proxy<OrganizationsController> _organizationsController = host.Controller<OrganizationsController>();

    [Fact]
    public async Task CreateAttribute_ShouldBeEnabledShownOnCardAndLast_WhenDefaultsAreNotSent()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);

        await CreateAttributeAsync(organization.Id, ownerId, new CreateAttributeRequest
        {
            Name = "Story points",
            Color = "#000000",
            Type = AttributeType.Integer,
        });

        var attributes = await GetAttributesAsync(organization.Id, ownerId);
        var storyPoints = attributes.Last();
        Assert.Equal("Story points", storyPoints.Name);
        Assert.True(storyPoints.IsEnabled);
        Assert.True(storyPoints.ShowOnCard);
    }

    [Fact]
    public async Task CreateAttribute_ShouldSaveDefaults_WhenTheyAreSent()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);

        await CreateAttributeAsync(organization.Id, ownerId, new CreateAttributeRequest
        {
            Name = "Story points",
            Color = "#000000",
            Type = AttributeType.Integer,
            IsEnabled = false,
            ShowOnCard = false,
        });

        var storyPoints = (await GetAttributesAsync(organization.Id, ownerId)).Single(x => x.Name == "Story points");
        Assert.False(storyPoints.IsEnabled);
        Assert.False(storyPoints.ShowOnCard);
    }

    [Fact]
    public async Task UpdateAttribute_ShouldKeepDefaults_WhenTheyAreNotSent()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var priorityId = AttributeId(organization, "Priority");
        await testScope.Database.Attributes
            .Where(x => x.Id == priorityId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsEnabled, false)
                .SetProperty(x => x.ShowOnCard, false));

        await UpdateAttributeAsync(organization.Id, ownerId, priorityId, new UpdateAttributeRequest
        {
            Name = "Urgency",
            Color = "#000000",
            ListValues = null,
        });

        var priority = (await GetAttributesAsync(organization.Id, ownerId)).Single(x => x.Id == priorityId);
        Assert.Equal("Urgency", priority.Name);
        Assert.False(priority.IsEnabled);
        Assert.False(priority.ShowOnCard);
    }

    [Fact]
    public async Task UpdateAttribute_ShouldChangeDefaults_WhenTheyAreSent()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var priorityId = AttributeId(organization, "Priority");

        await UpdateAttributeAsync(organization.Id, ownerId, priorityId, new UpdateAttributeRequest
        {
            Name = "Priority",
            Color = "#000000",
            ListValues = null,
            IsEnabled = false,
            ShowOnCard = true,
        });

        var priority = (await GetAttributesAsync(organization.Id, ownerId)).Single(x => x.Id == priorityId);
        Assert.False(priority.IsEnabled);
        Assert.True(priority.ShowOnCard);
    }

    [Fact]
    public async Task ReorderAttributes_ShouldListAttributesInNewOrder_WhenEveryAttributeIsGiven()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        long[] newOrder =
        [
            AttributeId(organization, "Estimate"),
            AttributeId(organization, "Type"),
            AttributeId(organization, "Priority"),
        ];

        await ReorderAsync(organization.Id, ownerId, newOrder);

        var attributes = await GetAttributesAsync(organization.Id, ownerId);
        Assert.Equal(["Estimate", "Type", "Priority"], attributes.Select(x => x.Name));
    }

    [Fact]
    public async Task ReorderAttributes_ShouldReturnBadRequestAndKeepOrder_WhenAnAttributeIsMissing()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        long[] newOrder = [AttributeId(organization, "Estimate"), AttributeId(organization, "Type")];

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => ReorderAsync(organization.Id, ownerId, newOrder));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        var attributes = await GetAttributesAsync(organization.Id, ownerId);
        Assert.Equal(["Type", "Priority", "Estimate"], attributes.Select(x => x.Name));
    }

    [Fact]
    public async Task ReorderAttributes_ShouldReturnBadRequest_WhenAnAttributeIsFromAnotherOrganization()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId);
        var otherOrganization = await testScope.InitializeOrganization(ownerId, org => org.AddTextAttribute("Foreign"));
        long[] newOrder =
        [
            AttributeId(organization, "Estimate"),
            AttributeId(organization, "Type"),
            otherOrganization.GetAttribute(0).Id,
        ];

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => ReorderAsync(organization.Id, ownerId, newOrder));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task ReorderAttributes_ShouldReturnNotFound_WhenCallerCannotManageAttributes()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await InitializeAsync(testScope, ownerId, org => org
            .AddUser(memberId, builder => builder
                .SetGlobalAccessLevel(x => x.CanRead = true)));
        long[] newOrder =
        [
            AttributeId(organization, "Estimate"),
            AttributeId(organization, "Type"),
            AttributeId(organization, "Priority"),
        ];

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => ReorderAsync(organization.Id, memberId, newOrder));

        // As for every admin action: no admin access looks like no such organization.
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

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
                .AddIntegerAttribute("Estimate");
            configure?.Invoke(org);
        });
    }

    private static long AttributeId(Organization organization, string name)
    {
        return organization.Attributes!.Single(x => x.Name == name).Id;
    }

    private Task<AttributeDto[]> GetAttributesAsync(long organizationId, Guid userId)
    {
        return _organizationsController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.GetAttributes(CancellationToken.None));
    }

    private Task<long> CreateAttributeAsync(long organizationId, Guid userId, CreateAttributeRequest request)
    {
        return _adminController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.CreateAttribute(request, CancellationToken.None));
    }

    private Task UpdateAttributeAsync(long organizationId, Guid userId, long attributeId, UpdateAttributeRequest request)
    {
        return _adminController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.UpdateAttribute(attributeId, request, CancellationToken.None));
    }

    private Task ReorderAsync(long organizationId, Guid userId, long[] attributeIds)
    {
        var request = new ReorderAttributesRequest { AttributeIds = attributeIds };
        return _adminController
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.ReorderAttributes(request, CancellationToken.None));
    }
}
