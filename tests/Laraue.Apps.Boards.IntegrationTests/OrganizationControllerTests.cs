using System.Net;
using System.Net.Http.Json;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiHost;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class OrganizationControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<OrganizationsController> _organizationsController = host.Controller<OrganizationsController>();
    private readonly Proxy<AdminOrganizationsController> _adminOrganizationsController = host.Controller<AdminOrganizationsController>();
    private readonly Proxy<SpacesController> _spacesController = host.Controller<SpacesController>();
    private readonly Proxy<IssuesController> _issuesController = host.Controller<IssuesController>();

    [Fact]
    public async Task CreateOrganization_ShouldCreateNewOrganizationWithDefaults_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        
        var response = await _organizationsController
            .WithUserAuthorization(userId)
            .Execute(x => x.Create(
                new CreateOrganizationRequest
                {
                    Name = "Org 1",
                    Color = "#ffffff",
                    Slug = "org"
                }));

        var organizations = await testScope.Database.Organizations.ToListAsyncEF();
        
        var organization = Assert.Single(organizations);
        Assert.Equal("Org 1", organization.Name);
        Assert.Equal("#ffffff", organization.Color);
        Assert.Equal(userId, organization.OwnerId);
        Assert.Equal(response!.Id, organization.Id);
        Assert.Equal("org", response.Slug);
        Assert.Equal(4, response.SlugPostfix.Length);
        Assert.Equal(8, organization.JoinCode!.Length);
        Assert.True(organization.CreatedAt != default);
        Assert.True(organization.UpdatedAt != default);
        
        var organizationUsers = await testScope.Database.OrganizationUsers.ToListAsyncEF();
        var organizationUser = Assert.Single(organizationUsers);
        Assert.Equal(userId, organizationUser.UserId);
        Assert.Equal(organization.Id, organizationUser.OrganizationId);
        Assert.True(organizationUser.CanCreateSpaces);
        Assert.True(organizationUser.CanUpdateSpaces);
        Assert.True(organizationUser.CanDeleteSpaces);
        Assert.True(organizationUser.CanRead);
        Assert.Equal(AdminAccessLevel.All, organizationUser.AdminAccessLevel);
        
        var spaces = await testScope.Database.Spaces.ToListAsyncEF();
        var space = Assert.Single(spaces);
        Assert.Equal("Default Space", space.Name);
        Assert.False(string.IsNullOrEmpty(space.Color));
        Assert.Equal(userId, space.CreatorId);
        Assert.Equal(organization.Id, space.OrganizationId);
        Assert.True(space.CreatedAt != default);
        Assert.True(space.UpdatedAt != default);
        Assert.True(space.IsDefault);
        
        var epics = await testScope.Database.Epics.ToListAsyncEF();
        var epic = Assert.Single(epics);
        Assert.Equal("Backlog", epic.Name);
        Assert.False(string.IsNullOrEmpty(epic.Color));
        Assert.Equal(userId, epic.UserId);
        Assert.Equal(space.Id, epic.SpaceId);
        Assert.True(epic.CreatedAt != default);
        Assert.True(epic.UpdatedAt != default);
        Assert.True(epic.TouchedAt != default);
        Assert.True(epic.IsDefault);
    }
    
    [Fact]
    public async Task User_ShouldViewOwnedAndParticipatingOrganizations_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var user2Id = await testScope.CreateUser();
        
        await testScope.InitializeOrganization(userId, org => org
            .WithName("Org 1")
            .WithColor("#ffffff")
            .AddUser(user2Id, builder => builder
                .SetGlobalAccessLevel(x => x.CanCreateSpaces = true)
                .SetAdminAccessLevel(AdminAccessLevel.DeleteOrganization)));
        
        await testScope.InitializePersonalOrganization(user2Id, org => org
            .WithName("Org 2")
            .WithColor("#000000"));
        
        // First user see only owned organization
        var organizations = await _organizationsController
            .WithUserAuthorization(userId)
            .Execute(x => x.GetOrganizations());
        var organization = Assert.Single(organizations!);
        
        Assert.True(organization.CanDelete);
        Assert.False(organization.CanLeave);
        Assert.True(organization.CanUpdate);
        Assert.Equal("Org 1", organization.Name);
        Assert.Equal("#ffffff", organization.Color);
        
        // Second user see owned organization and organization where he was added
        organizations = await _organizationsController
            .WithUserAuthorization(user2Id)
            .Execute(x => x.GetOrganizations());
        
        Assert.Equal(2, organizations!.Length);
        var (personalOrganization, additionalOrganization) = (organizations[1], organizations[0]);
        
        Assert.False(personalOrganization.IsPersonal);
        Assert.True(personalOrganization.CanDelete);
        Assert.True(personalOrganization.CanLeave);
        Assert.False(personalOrganization.CanUpdate);
        Assert.Equal("Org 1", personalOrganization.Name);
        Assert.Equal("#ffffff", personalOrganization.Color);
        
        Assert.True(additionalOrganization.IsPersonal);
        Assert.False(additionalOrganization.CanDelete);
        Assert.False(additionalOrganization.CanLeave);
        Assert.True(additionalOrganization.CanUpdate);
        Assert.Equal("Org 2", additionalOrganization.Name);
        Assert.Equal("#000000", additionalOrganization.Color);
    }
    
    [Fact]
    public async Task User_ShouldNotViewUnavailableOrganizations_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var nonPermittedUserId = await testScope.CreateUser();

        await testScope.InitializeOrganization(userId);
        
        var organizations = await _organizationsController
            .WithUserAuthorization(nonPermittedUserId)
            .Execute(x => x.GetOrganizations());
        
        Assert.Empty(organizations!);
    }
    
    [Fact]
    public async Task User_ShouldUpdateOwnedOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();

        var date1 = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var entity = await testScope.InitializeOrganization(userId, org => org
            .WithName("Org 1")
            .WithColor("#ffffff")
            .WithTimestamp(date1));
        
        await _adminOrganizationsController
            .WithUserAuthorization(userId)
            .Execute(x => x.Update(
                entity.Id,
                new EditOrganizationRequest
                {
                    Name = "Org 2",
                    Color = "#000000",
                    Slug = "slug"
                }));

        var organizations = await testScope.Database.Organizations.ToListAsyncEF();
        
        var organization = Assert.Single(organizations);
        Assert.Equal("Org 2", organization.Name);
        Assert.Equal("#000000", organization.Color);
        Assert.Equal(date1, organization.CreatedAt);
        Assert.Equal("slug", organization.Slug);
        Assert.True(organization.UpdatedAt > date1);
    }
    
    [Fact]
    public async Task User_ShouldUpdateOrganization_WhenHasAccess()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();

        var entity = await testScope.InitializeOrganization(userId, org => org
            .AddUser(participatorId, builder => builder
                .SetAdminAccessLevel(AdminAccessLevel.UpdateOrganization)));
        
        await _adminOrganizationsController
            .WithUserAuthorization(participatorId)
            .Execute(x => x.Update(
                entity.Id,
                new EditOrganizationRequest
                {
                    Name = "Org 2",
                    Color = "#000000",
                    Slug =  "slug"
                }));
    }
    
    [Fact]
    public async Task User_ShouldNotUpdateOrganization_WhenHasNoAccess()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();

        var entity = await testScope.InitializeOrganization(userId, org => org
            .AddUser(participatorId, builder => builder
                .SetAdminAccessLevel(AdminAccessLevel.None)));
        
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _adminOrganizationsController
            .WithUserAuthorization(participatorId)
            .Execute(x => x.Update(
                entity.Id,
                new EditOrganizationRequest
                {
                    Name = "Org 2",
                    Color = "#000000",
                    Slug =  "slug"
                })));
        
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }
    
    [Fact]
    public async Task User_ShouldDeleteOwnedOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();

        var entity = await testScope.InitializeOrganization(userId);
        
        await _adminOrganizationsController
            .WithUserAuthorization(userId)
            .Execute(x => x.Delete(entity.Id));

        var organizations = await testScope.Database.Organizations.ToListAsyncEF();
        Assert.Empty(organizations);
    }
    
    [Fact]
    public async Task User_ShouldDeleteOrganization_WhenHasAccess()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();

        var entity = await testScope.InitializeOrganization(userId, org => org
            .AddUser(participatorId, builder => builder
                .SetAdminAccessLevel(AdminAccessLevel.DeleteOrganization)));
        
        await _adminOrganizationsController
            .WithUserAuthorization(userId)
            .Execute(x => x.Delete(entity.Id));
        
        var organizations = await testScope.Database.Organizations.ToListAsyncEF();
        Assert.Empty(organizations);
    }
    
    [Fact]
    public async Task User_ShouldNotDeleteOrganization_WhenHasNotAccess()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();

        var entity = await testScope.InitializeOrganization(userId, org => org
            .AddUser(participatorId, builder => builder
                .SetAdminAccessLevel(AdminAccessLevel.UpdateOrganization)));
        
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _adminOrganizationsController
            .WithUserAuthorization(participatorId)
            .Execute(x => x.Delete(entity.Id)));
        
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }
    
    [Fact]
    public async Task User_ShouldJoinOrganization_WhenIsNotOrganizationMember()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var newUserId = await testScope.CreateUser();

        var organization = new Organization
        {
            Name = "Org 1",
            OwnerId = ownerId,
            JoinCode = "abc",
            Color = "#ffffff"
        };
        
        testScope.Database.Organizations.Add(organization);
        await testScope.Database.SaveChangesAsync();

        await _organizationsController
            .WithUserAuthorization(newUserId)
            .Execute(x => x.Join("abc"));
        
        var organizationUsers = await testScope.Database.OrganizationUsers.ToListAsyncEF();
        var organizationUser = Assert.Single(organizationUsers);
        
        Assert.Equal(organization.Id, organizationUser.OrganizationId);
        Assert.Equal(newUserId, organizationUser.UserId);
        
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>  _organizationsController
            .WithUserAuthorization(newUserId)
            .Execute(x => x.Join("abc")));
        
        Assert.Equal(HttpStatusCode.NotAcceptable, exception.StatusCode);
    }
    
    [Fact]
    public async Task User_ShouldNotJoinOrganization_WhenCodeIsWrong()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var newUserId = await testScope.CreateUser();

        var organization = new Organization
        {
            Name = "Org 1",
            OwnerId = ownerId,
            JoinCode = "abc",
            Color = "#ffffff"
        };
        
        testScope.Database.Organizations.Add(organization);
        await testScope.Database.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _organizationsController
            .WithUserAuthorization(newUserId)
            .Execute(x => x.Join("def")));
        
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }
    
    [Fact]
    public async Task User_ShouldLeaveOrganization_WhenIsParticipator()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var newUserId = await testScope.CreateUser();

        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(newUserId));
        
        // Ensure that user was in DB
        var organizationUsers = await testScope.Database.OrganizationUsers.ToListAsyncEF();
        Assert.NotNull(organizationUsers.FirstOrDefault(x => x.UserId == newUserId));
        
        await _organizationsController
            .WithUserAuthorization(newUserId)
            .Execute(x => x.Leave(organization.Id));
        
        // Ensure that now the record is missing
        organizationUsers = await testScope.Database.OrganizationUsers.ToListAsyncEF();
        Assert.Null(organizationUsers.FirstOrDefault(x => x.UserId == newUserId));
    }

    [Fact]
    public async Task Owner_ShouldNotLeaveOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _organizationsController
            .WithUserAuthorization(ownerId)
            .Execute(x => x.Leave(organization.Id)));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task User_ShouldSetEmployeeDirectAccessInOwnedOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var userIdToReceivePermissions = await testScope.CreateUser();

        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(userIdToReceivePermissions));
        
        var space = organization.Spaces![0];
        var epic = space.Epics![0];
        var organizationUser = organization.Users![1];

        var request = new SetPermissionsRequest
        {
            UserPermissions = new UserPermissions
            {
                Direct = new Dictionary<string, DirectSpaceAccessLevel>
                {
                    [space.Key] = new()
                    {
                        CanDeleteIssues = true,
                    }
                },
                Admin = AdminAccessLevel.Manage,
                Global = new GlobalAccessLevels
                {
                    CanCreateSpaces = true,
                }
            }
        };
        
        await _adminOrganizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.SetUserPermissions(organizationUser.Id, request));
        
        organizationUser = await testScope.Database.OrganizationUsers.FirstAsyncEF(x => x.Id == organizationUser.Id);
        Assert.True(organizationUser.CanCreateSpaces);
        Assert.True(organizationUser.CanCreateEpics);
        Assert.True(organizationUser.CanCreateIssues);
        Assert.True(organizationUser.CanRead);
        
        Assert.False(organizationUser.CanDeleteSpaces);
        Assert.False(organizationUser.CanDeleteEpics);
        Assert.False(organizationUser.CanDeleteIssues);
        
        Assert.Equal(AdminAccessLevel.Manage, organizationUser.AdminAccessLevel);
        
        var spaceOrganizationUser = await testScope.Database.DirectSpacePermissions.FirstAsyncEF(x => x.OrganizationUserId == organizationUser.Id);
        Assert.Equal(organizationUser.Id, spaceOrganizationUser.OrganizationUserId);
        Assert.Equal(space.Id, spaceOrganizationUser.SpaceId);
        
        Assert.True(spaceOrganizationUser.CanCreateEpics);
        Assert.True(spaceOrganizationUser.CanCreateIssues);
        Assert.False(spaceOrganizationUser.CanDelete);
        Assert.False(spaceOrganizationUser.CanDeleteEpics);
        Assert.True(spaceOrganizationUser.CanDeleteIssues);
        Assert.True(spaceOrganizationUser.CanRead);
    }
    
    [Fact]
    public async Task User_ShouldNotSetDirectAccessInOwnedOrganization_WhenDataIsIncorrect()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var userIdToReceivePermissions = await testScope.CreateUser();

        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(userIdToReceivePermissions));
        
        var space = organization.GetSpace(0);
        var epic = organization.GetEpic(0, 0);
        var organizationUser = organization.Users![1];

        var request = new SetPermissionsRequest
        {
            UserPermissions = new UserPermissions
            {
                Direct = new Dictionary<string, DirectSpaceAccessLevel>
                {
                    [space.Key] = new ()
                    {
                        CanDelete = true, // Attempt to set delete permission for default space
                    },
                    ["GGG"] = new () // Unexists space
                    {
                        CanCreateEpics = true,
                    }
                },
                Admin = AdminAccessLevel.Manage
            }
        };
        
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _adminOrganizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.SetUserPermissions(organizationUser.Id, request)));
        var badRequest = ex.HasInnerException<BadRequestException>();
        var exceptedErrors = new[]
        {
            $"Space: '{space.Key}'. Attempt to add delete permission to Default space",
            "Space: 'GGG'. Entity is not found",
        };
        Assert.Equal(exceptedErrors, badRequest.Errors["direct"]);
    }
    
    [Fact]
    public async Task User_ShouldSetEmployeeSectionAccess_WhenHasAccess()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var adminId = await testScope.CreateUser();
        var userIdToReceivePermissions = await testScope.CreateUser();
        
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(adminId, builder => builder
                .SetAdminAccessLevel(AdminAccessLevel.Manage))
            .AddUser(userIdToReceivePermissions));
        
        var organizationUser = organization.Users![2];

        var request = new SetPermissionsRequest
        {
            UserPermissions = new UserPermissions()
        };
        
        await _adminOrganizationsController
            .WithOrganizationAuthorization(organization.Id, adminId)
            .Execute(x => x.SetUserPermissions(organizationUser.Id, request));
    }
    
    [Fact]
    public async Task User_ShouldNotSetEmployeeAccess_WhenHasNoAccess()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var nonPermittedUserId = await testScope.CreateUser();
        var userIdToReceivePermissions = await testScope.CreateUser();
        
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(nonPermittedUserId, builder => builder.SetAdminAccessLevel(AdminAccessLevel.Manage))
            .AddUser(userIdToReceivePermissions));
        
        var organizationUser = organization.Users![2];

        var request = new SetPermissionsRequest
        {
            UserPermissions = new UserPermissions()
        };
        
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _adminOrganizationsController
            .WithOrganizationAuthorization(organization.Id, userIdToReceivePermissions)
            .Execute(x => x.SetUserPermissions(organizationUser.Id, request)));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }
    
    [Fact]
    public async Task User_ShouldViewPermissionsOfNewEmployeeInOwnedOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organizationUserId = await testScope.CreateUser();

        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(organizationUserId));

        var organizationUser = organization.Users![1];
        var permissions = await _adminOrganizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetUserPermissions(organizationUser.Id));

        Assert.NotNull(permissions);
        Assert.False(permissions.Global.CanCreateEpics);
        Assert.Equal(AdminAccessLevel.None, permissions.Admin);
        Assert.Empty(permissions.Direct);
    }

    [Fact]
    public async Task User_ShouldViewPermissionsOfNewEmployee_WhenHasAccess()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var adminId = await testScope.CreateUser();
        var organizationUserId = await testScope.CreateUser();

        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(adminId, builder => builder.SetAdminAccessLevel(AdminAccessLevel.Manage))
            .AddUser(organizationUserId));

        var organizationUser = organization.Users![2];
        var permissions = await _adminOrganizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetUserPermissions(organizationUser.Id));

        Assert.NotNull(permissions);
    }

    [Fact]
    public async Task User_ShouldNotViewPermissionsOfNewEmployee_WhenHasNotAccess()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var nonPermittedUserId = await testScope.CreateUser();
        var organizationUserId = await testScope.CreateUser();

        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(nonPermittedUserId, builder => builder.SetAdminAccessLevel(AdminAccessLevel.None))
            .AddUser(organizationUserId));

        var organizationUser = organization.Users![2];

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _adminOrganizationsController
            .WithOrganizationAuthorization(organization.Id, nonPermittedUserId)
            .Execute(x => x.GetUserPermissions(organizationUser.Id)));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }
    
    [Fact]
    public async Task User_ShouldViewEmployeePermissionsInOwnedOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organizationUserId = await testScope.CreateUser();

        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(organizationUserId, builder => builder
                .SetGlobalAccessLevel(x => x.CanCreateSpaces = true)));

        var organizationUser = organization.Users![1];

        var permissions = await _adminOrganizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetUserPermissions(organizationUser.Id));

        Assert.NotNull(permissions);
        Assert.Equal(AdminAccessLevel.None, permissions.Admin);
    }

    [Fact]
    public async Task User_ShouldRevokeAccess_WhenHasPermissionManagementPermission()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var adminId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();

        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(adminId, builder => builder.SetAdminAccessLevel(AdminAccessLevel.Manage))
            .AddUser(participatorId));

        var user = organization.Users![2];
        
        // Ensure that record exists
        var organizationUsers = await testScope.Database.OrganizationUsers.ToListAsyncEF();
        Assert.NotNull(organizationUsers.FirstOrDefault(x => x.UserId == participatorId));
        
        await _adminOrganizationsController
            .WithOrganizationAuthorization(organization.Id, adminId)
            .Execute(x => x.RevokeAccess(user.Id));
        
        // Ensure that now the record is missing
        organizationUsers = await testScope.Database.OrganizationUsers.ToListAsyncEF();
        Assert.Null(organizationUsers.FirstOrDefault(x => x.UserId == participatorId));
    }

    [Fact]
    public async Task User_ShouldRegenerateJoinCode_WhenHasManagePermission()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var adminId = await testScope.CreateUser();

        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(adminId, builder => builder.SetAdminAccessLevel(AdminAccessLevel.Manage)));
        
        var newCode = await _adminOrganizationsController
            .WithOrganizationAuthorization(organization.Id, adminId)
            .Execute(x => x.RegenerateCode());
        
        Assert.Equal(8, newCode!.Length);
        organization = await testScope.Database.Organizations.SingleAsyncEF();
        Assert.Equal(newCode, organization.JoinCode);
    }

    [Fact]
    public async Task Owner_ShouldRegenerateJoinCode_Always()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();

        var organization = await testScope.InitializeOrganization(ownerId);
        
        var newCode = await _adminOrganizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.RegenerateCode());
        
        Assert.Equal(8, newCode!.Length);
        organization = await testScope.Database.Organizations.SingleAsyncEF();
        Assert.Equal(newCode, organization.JoinCode);
    }
    
    [Fact]
    public async Task Login_ShouldReturnTokenForOwnedOrganization_Always()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();

        var organization = await testScope.InitializeOrganization(ownerId);
        
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
    public async Task Cookies_ShouldAuthenticateUserAndOrganizationRequests()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var authService = host.Services.GetRequiredService<IAuthService>();

        using var userClient = host.CreateClient();
        userClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCookies.User}={authService.CreateUserToken(userId)}");

        var loginResponse = await userClient.PostAsJsonAsync(
            "/api/organizations/login",
            new LoginRequest { OrganizationId = organization.Id });

        loginResponse.EnsureSuccessStatusCode();
        var setCookie = Assert.Single(
            loginResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith($"{AuthCookies.Organization}=", StringComparison.Ordinal));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);

        using var organizationClient = host.CreateClient();
        organizationClient.DefaultRequestHeaders.Add("Cookie", setCookie.Split(';')[0]);
        var spacesResponse = await organizationClient.GetAsync("/api/spaces");

        Assert.Equal(HttpStatusCode.OK, spacesResponse.StatusCode);
    }

    [Fact]
    public async Task Login_ShouldReturnTokenForOrganization_WhenHasAccess()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();

        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(participatorId));
        
        var organizationAuthToken = await _organizationsController
            .WithUserAuthorization(participatorId)
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
    public async Task Login_ShouldNotReturnTokenForOrganization_WhenHasNotAccess()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();

        var organization = await testScope.InitializeOrganization(ownerId);
        
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _organizationsController
            .WithUserAuthorization(participatorId)
            .Execute(x => x.Login(
                new LoginRequest
                {
                    OrganizationId = organization.Id
                })));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetOrganizationHistory_ShouldOnlyIncludeReadableSpaces_WhenUserHasSpaceLevelAccessOnly()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var participatorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o
                .AddUser(participatorId, u => u
                    .SetSpaceAccessLevel(1, x => x.CanRead = true))
                .AddIssueToDefaultStatus(userId, issue => issue.WithContent("Default space issue"))
                .AddSpace(userId, space => space
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, issue => issue.WithContent("Second space issue")))));

        var defaultSpaceIssue = organization.GetIssueData(0, 0, 0, 0);
        var secondSpaceIssue = organization.GetIssueData(1, 1, 0, 0);

        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Update(defaultSpaceIssue.Key, new UpdateIssueRequest
            {
                AssigneeId = userId,
                Content = "Default space issue updated",
            }));

        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Update(secondSpaceIssue.Key, new UpdateIssueRequest
            {
                AssigneeId = userId,
                Content = "Second space issue updated",
            }));

        var request = new GetOrganizationHistoryRequest
        {
            Pagination = new PaginationData
            {
                Page = 0,
                PerPage = 10,
            }
        };

        var historyData = await _organizationsController
            .WithOrganizationAuthorization(organization.Id, participatorId)
            .Execute(x => x.GetOrganizationHistory(request));

        var historyItem = Assert.Single(historyData!.Data);
        Assert.Equal(secondSpaceIssue.Key, historyItem.IssueKey);
        Assert.Equal(LogEntityType.Issue, historyItem.EntityType);
        Assert.Equal(LogAction.Update, historyItem.Action);
    }

    [Fact]
    public async Task GetOrganizationHistory_ShouldOnlyIncludeEntriesInDateRange_WhenDateFromAndDateToProvided()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o.AddIssueToDefaultStatus(userId, issue => issue.WithContent("Issue")));

        var issueData = organization.GetIssueData(0, 0, 0, 0);

        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Update(issueData.Key, new UpdateIssueRequest
            {
                AssigneeId = userId,
                Content = "Updated 1",
            }));

        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Update(issueData.Key, new UpdateIssueRequest
            {
                AssigneeId = userId,
                Content = "Updated 2",
            }));

        var logs = await testScope.Database.OrganizationLogs
            .AsTracking()
            .Where(x => x.OrganizationId == organization.Id)
            .OrderBy(x => x.Id)
            .ToListAsync();

        var oldEntry = logs[0];
        oldEntry.CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var recentEntry = logs[1];
        recentEntry.CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await testScope.Database.SaveChangesAsync();

        var request = new GetOrganizationHistoryRequest
        {
            DateFrom = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Pagination = new PaginationData
            {
                Page = 0,
                PerPage = 10,
            }
        };

        var historyData = await _organizationsController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetOrganizationHistory(request));

        var historyItem = Assert.Single(historyData!.Data);
        Assert.Equal(recentEntry.CreatedAt, historyItem.CreatedAt);
    }

    [Fact]
    public async Task GetVisibleUsers_ShouldReturnOnlySpaceMembers_WhenSpaceKeyProvided()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser(x =>
        {
            x.TelegramUserName = "aa";
            x.Color = "#111111";
        });
        var spaceMemberId = await testScope.CreateUser(x =>
        {
            x.TelegramUserName = "bb";
            x.Color = "#222222";
        });
        var otherSpaceMemberId = await testScope.CreateUser(x => x.TelegramUserName = "cc");

        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(spaceMemberId, b => b
                .SetSpaceAccessLevel(0, x => x.CanRead = true)) // member of the DEF space
            .AddSpace(ownerId)
            .AddUser(otherSpaceMemberId, b => b
                .SetSpaceAccessLevel(1, x => x.CanRead = true))); // member of the ADD space

        var defSpaceKey = organization.Spaces![0].Key;

        var members = await _organizationsController
            .WithOrganizationAuthorization(organization.Id, otherSpaceMemberId)
            .Execute(x => x.GetVisibleUsers(defSpaceKey));

        Assert.Equal(2, members!.Length);
        Assert.Equal(["AA", "BB"], members.Select(x => x.Initials).OrderBy(x => x));
        Assert.DoesNotContain(members, x => x.UserId == otherSpaceMemberId);
    }

    [Fact]
    public async Task GetVisibleUsers_ShouldReturnUsersAcrossAllReadableSpaces_WhenSpaceKeyOmitted()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser(x => x.TelegramUserName = "aa");
        var spaceMemberId = await testScope.CreateUser(x => x.TelegramUserName = "bb");
        var otherSpaceMemberId = await testScope.CreateUser(x => x.TelegramUserName = "cc");

        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(spaceMemberId, b => b
                .SetSpaceAccessLevel(0, x => x.CanRead = true)) // member of the DEF space only
            .AddSpace(ownerId)
            .AddUser(otherSpaceMemberId, b => b
                .SetSpaceAccessLevel(1, x => x.CanRead = true))); // member of the ADD space only

        // Only readable space for spaceMemberId is DEF, so only its members should come back.
        var members = await _organizationsController
            .WithOrganizationAuthorization(organization.Id, spaceMemberId)
            .Execute(x => x.GetVisibleUsers(null));

        Assert.Equal(2, members!.Length);
        Assert.Contains(members, x => x.UserId == ownerId);
        Assert.Contains(members, x => x.UserId == spaceMemberId);
        Assert.DoesNotContain(members, x => x.UserId == otherSpaceMemberId);

        // Owner has org-wide read, so every space's members are visible.
        members = await _organizationsController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetVisibleUsers(null));

        Assert.Equal(3, members!.Length);
        Assert.True(Assert.Single(members, x => x.UserId == ownerId).IsCurrentUser);
    }
}
