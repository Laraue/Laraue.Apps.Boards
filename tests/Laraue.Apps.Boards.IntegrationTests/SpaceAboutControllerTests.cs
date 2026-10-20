using System.Net;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class SpaceAboutControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<SpacesController> _controller = host.Controller<SpacesController>();

    [Fact]
    public async Task GetAbout_ShouldReturnEmptyAboutEditableByOwner_WhenNothingIsSet()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org.AddSpace(ownerId, "ABC"));

        var about = await _controller
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.GetAbout("ABC", CancellationToken.None));

        Assert.Null(about.Description);
        Assert.Empty(about.Links);
        Assert.True(about.CanEdit);
    }

    [Fact]
    public async Task GetAbout_ShouldReturnDescriptionAndLinks_WhenEditorSetThem()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var readerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, "ABC")
            .AddUser(readerId, builder => builder
                .SetGlobalAccessLevel(x => x.CanRead = true)));

        await AsOwner(organization.Id, ownerId)
            .Execute(x => x.UpdateDescription(
                "ABC",
                new UpdateSpaceDescriptionRequest { Description = "  The **Boards** tracker.  " },
                CancellationToken.None));
        var backend = await CreateLinkAsync(organization.Id, ownerId, "ABC", "https://github.com/Laraue/Laraue.Apps.Boards", "Backend");
        var web = await CreateLinkAsync(organization.Id, ownerId, "ABC", "https://github.com/Laraue/laraue-boards", "Web app");

        var about = await _controller
            .WithOrganizationAuthorization(organization.Id, readerId)
            .Execute(x => x.GetAbout("ABC", CancellationToken.None));

        Assert.Equal("The **Boards** tracker.", about.Description);
        Assert.Equal([backend.Id, web.Id], about.Links.Select(x => x.Id));
        Assert.Equal("Web app", about.Links[1].Description);
        Assert.False(about.CanEdit);
    }

    [Fact]
    public async Task GetAbout_ShouldReturnNotFound_WhenCallerCannotReadSpace()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var strangerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, "ABC")
            .AddUser(strangerId));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _controller
            .WithOrganizationAuthorization(organization.Id, strangerId)
            .Execute(x => x.GetAbout("ABC", CancellationToken.None)));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task CreateLink_ShouldReturnForbidden_WhenCallerCanOnlyReadSpace()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var readerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, "ABC")
            .AddUser(readerId, builder => builder
                .SetGlobalAccessLevel(x => x.CanRead = true)));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateLinkAsync(organization.Id, readerId, "ABC", "https://example.com/docs", "Docs"));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task CreateLink_ShouldReturnBadRequest_WhenUrlIsAlreadyALinkOfTheSpace()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org.AddSpace(ownerId, "ABC"));
        await CreateLinkAsync(organization.Id, ownerId, "ABC", "https://example.com/docs", "Docs");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateLinkAsync(organization.Id, ownerId, "ABC", "https://example.com/docs", "The same docs"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task CreateLink_ShouldAddSameUrlWithOwnDescription_WhenItIsInAnotherSpace()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, "ABC")
            .AddSpace(ownerId, "XYZ"));
        await CreateLinkAsync(organization.Id, ownerId, "ABC", "https://github.com/Laraue/ui-kit", "The code we change");

        await CreateLinkAsync(organization.Id, ownerId, "XYZ", "https://github.com/Laraue/ui-kit", "The UI kit we use");

        var about = await AsOwner(organization.Id, ownerId)
            .Execute(x => x.GetAbout("XYZ", CancellationToken.None));
        Assert.Equal("The UI kit we use", Assert.Single(about.Links).Description);
    }

    [Fact]
    public async Task CreateLink_ShouldReturnBadRequest_WhenUrlIsNotAUrl()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org.AddSpace(ownerId, "ABC"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateLinkAsync(organization.Id, ownerId, "ABC", "not a url", "Docs"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task CreateLink_ShouldReturnBadRequest_WhenSpaceHasMaximumNumberOfLinks()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org.AddSpace(ownerId, "ABC"));
        var spaceId = organization.Spaces!.Single(x => x.Key == "ABC").Id;
        testScope.Database.SpaceLinks.AddRange(Enumerable.Range(0, Constraints.MaxSpaceLinksPerSpace)
            .Select(i => new SpaceLink
            {
                SpaceId = spaceId,
                Url = $"https://example.com/{i}",
                Description = "Link",
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = ownerId,
            }));
        await testScope.Database.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateLinkAsync(organization.Id, ownerId, "ABC", "https://example.com/one-more", "Docs"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateLink_ShouldChangeUrlAndDescription_WhenLinkBelongsToSpace()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org.AddSpace(ownerId, "ABC"));
        var link = await CreateLinkAsync(organization.Id, ownerId, "ABC", "https://example.com/old", "Old");

        await AsOwner(organization.Id, ownerId)
            .Execute(x => x.UpdateLink(
                "ABC",
                link.Id,
                new SpaceLinkRequest { Url = "https://example.com/new", Description = "New" },
                CancellationToken.None));

        var about = await AsOwner(organization.Id, ownerId)
            .Execute(x => x.GetAbout("ABC", CancellationToken.None));
        var updated = Assert.Single(about.Links);
        Assert.Equal("https://example.com/new", updated.Url);
        Assert.Equal("New", updated.Description);
    }

    [Fact]
    public async Task DeleteLink_ShouldReturnNotFound_WhenLinkBelongsToAnotherSpace()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, "ABC")
            .AddSpace(ownerId, "XYZ"));
        var link = await CreateLinkAsync(organization.Id, ownerId, "ABC", "https://example.com/docs", "Docs");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => AsOwner(organization.Id, ownerId)
            .Execute(x => x.DeleteLink("XYZ", link.Id, CancellationToken.None)));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task DeleteLink_ShouldRemoveLink_WhenLinkBelongsToSpace()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org.AddSpace(ownerId, "ABC"));
        var link = await CreateLinkAsync(organization.Id, ownerId, "ABC", "https://example.com/docs", "Docs");

        await AsOwner(organization.Id, ownerId)
            .Execute(x => x.DeleteLink("ABC", link.Id, CancellationToken.None));

        var about = await AsOwner(organization.Id, ownerId)
            .Execute(x => x.GetAbout("ABC", CancellationToken.None));
        Assert.Empty(about.Links);
    }

    private Proxy<SpacesController> AsOwner(long organizationId, Guid userId)
    {
        return _controller.WithOrganizationAuthorization(organizationId, userId);
    }

    private Task<SpaceLinkDto> CreateLinkAsync(long organizationId, Guid userId, string spaceKey, string url, string description)
    {
        return _controller
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.CreateLink(
                spaceKey,
                new SpaceLinkRequest { Url = url, Description = description },
                CancellationToken.None));
    }
}
