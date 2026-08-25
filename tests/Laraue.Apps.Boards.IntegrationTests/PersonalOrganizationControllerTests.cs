using System.Net;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class PersonalOrganizationControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<OrganizationsController> _organizationsController = host.Controller<OrganizationsController>();
    private readonly Proxy<AdminOrganizationsController> _adminOrganizationsController = host.Controller<AdminOrganizationsController>();
    private readonly Proxy<SpacesController> _spacesController = host.Controller<SpacesController>();
    
    [Fact]
    public async Task User_ShouldUpdatePersonalOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        
        var timestamp = DateTime.UtcNow;
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            setup => setup
                .WithTimestamp(timestamp));

        await _adminOrganizationsController
            .WithUserAuthorization(userId)
            .Execute(x => x.Update(
                organization.Id,
                new EditOrganizationRequest
                {
                    Name = "Org 2",
                    Color = "#000000",
                    Slug =  "slug"
                }));

        var organizations = await testScope.Database.Organizations.ToListAsyncEF();
        
        organization = Assert.Single(organizations);
        Assert.Equal("Org 2", organization.Name);
        Assert.Equal("#000000", organization.Color);
        Assert.Equal("slug", organization.Slug);
        Assert.Equal(timestamp, organization.CreatedAt, new TimeSpan(10));
        Assert.True(organization.UpdatedAt > timestamp);
    }
    
    [Fact]
    public async Task User_ShouldNotUpdateSomeonePersonalOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var nonPermittedUserId = await testScope.CreateUser();

        var organization = await testScope.InitializePersonalOrganization(userId);
        
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _adminOrganizationsController
            .WithUserAuthorization(nonPermittedUserId)
            .Execute(x => x.Update(
                organization.Id,
                new EditOrganizationRequest
                {
                    Name = "Org 2",
                    Color = "#000000",
                    Slug =  "slug"
                })));
        
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }
    
    [Fact]
    public async Task User_ShouldNotDeletePersonalOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(userId);
        
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _adminOrganizationsController
            .WithUserAuthorization(userId)
            .Execute(x => x.Delete(organization.Id)));

        var forbidden = exception.HasInnerException<NotFoundException>();
        Assert.Equal($"Organization: {organization.Id}. Deleting organization requires 'DeleteOrganization' admin access", forbidden.Message);
    }
    
    [Fact]
    public async Task User_ShouldNotDeleteSomeonePersonalOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var nonPermittedUserId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(userId);
        
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _adminOrganizationsController
            .WithUserAuthorization(nonPermittedUserId)
            .Execute(x => x.Delete(organization.Id)));
        
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }
    
    [Fact]
    public async Task Login_ShouldReturnValidOrganizationToken_Always()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(ownerId);
        
        var organizationAuthToken = await _organizationsController
            .WithUserAuthorization(ownerId)
            .Execute(x => x.Login(
                new LoginRequest
                {
                    OrganizationId = organization.Id
                }));
        
        Assert.NotNull(organizationAuthToken);
        
        // Spaces are available with organization token
        var getSpacesResponse = await _spacesController
            .WithAuthorizationToken(organizationAuthToken)
            .Execute(x => x.GetAll());
        
        Assert.NotNull(getSpacesResponse);
    }
    
    [Fact]
    public async Task User_ShouldViewOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(ownerId);
        
        var result = await _organizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetOrganization());
        
        Assert.NotNull(result);
        Assert.False(result.CanManage);
        Assert.True(result.CanMassMove);
        Assert.True(result.CanCreateSpaces);
    }
    
    [Fact]
    public async Task User_ShouldCreateListAttribute_Always()
    {
        using var testScope = host.CreateTestScope();
        
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(userId);

        var attributeId = await _adminOrganizationsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.CreateAttribute(
                new CreateAttributeRequest
                {
                    Name = "Color",
                    Color = "#000000",
                    Type = AttributeType.List,
                    ListValues = new []
                    {
                        new NewAttributeListValueDto { Name = "Red" },
                        new NewAttributeListValueDto { Name = "Green" },
                    }
                }));

        var attributes = await testScope.Database.Attributes.ToListAsyncEF();
        var attribute = Assert.Single(attributes);
        
        Assert.Equal("Color", attribute.Name);
        Assert.Equal("#000000", attribute.Color);
        Assert.Equal(AttributeType.List, attribute.AttributeType);
        
        var attributeListValues = await testScope.Database.AttributeListValues.OrderBy(x => x.Id).ToListAsyncEF();
        Assert.Equal(2, attributeListValues.Count);
        Assert.Equal(["Red", "Green"], attributeListValues.Select(x => x.Value));
        Assert.All(attributeListValues, v => Assert.Equal(attributeId, v.AttributeId));
    }
    
    [Fact]
    public async Task User_ShouldCreateTextAttribute_Always()
    {
        using var testScope = host.CreateTestScope();
        
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(userId);

        await _adminOrganizationsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.CreateAttribute(
                new CreateAttributeRequest
                {
                    Name = "Jira number",
                    Color = "#111111",
                    Type = AttributeType.Text
                }));

        var attributes = await testScope.Database.Attributes.ToListAsyncEF();
        var attribute = Assert.Single(attributes);
        
        Assert.Equal("Jira number", attribute.Name);
        Assert.Equal("#111111", attribute.Color);
        Assert.Equal(AttributeType.Text, attribute.AttributeType);
    }
    
    [Fact]
    public async Task User_ShouldGetAttributes_Always()
    {
        using var testScope = host.CreateTestScope();
        
        var userId = await testScope.CreateUser();
        var organization = await testScope
            .InitializePersonalOrganization(userId, o => o
                .AddTextAttribute("Jira number")
                .AddListAttribute("Color", ["Red", "Green"]));

        var attributes = await _organizationsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetAttributes());
        
        Assert.Equal(2, attributes!.Length);
        
        Assert.Equal("Jira number", attributes[0].Name);
        Assert.Equal(AttributeType.Text, attributes[0].Type);
        
        Assert.Equal("Color", attributes[1].Name);
        Assert.Equal(AttributeType.List, attributes[1].Type);
        Assert.Equal(["Red", "Green"], attributes[1].ListValues.Select(x => x.Name));
    }
    
    [Fact]
    public async Task User_ShouldDeleteAttributes_Always()
    {
        using var testScope = host.CreateTestScope();
        
        var userId = await testScope.CreateUser();
        var organization = await testScope
            .InitializePersonalOrganization(userId, o => o
                .AddListAttribute("Color", ["Red", "Green"]));

        var firstAttributeId = organization.GetAttribute(0).Id;
        await _adminOrganizationsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.DeleteAttribute(firstAttributeId));
        
        Assert.Empty(await testScope.Database.Attributes.ToListAsyncEF());
        Assert.Empty(await testScope.Database.AttributeListValues.ToListAsyncEF());
    }
}
