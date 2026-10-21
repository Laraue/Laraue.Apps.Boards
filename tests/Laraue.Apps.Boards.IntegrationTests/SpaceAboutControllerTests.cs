using System.Net;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.EntityFrameworkCore;

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

        var about = await GetAboutAsync(organization.Id, ownerId, "ABC");

        Assert.Null(about.Description);
        Assert.Empty(about.Links);
        Assert.True(about.CanEdit);
    }

    [Fact]
    public async Task Update_ShouldSaveDescriptionAndLinks_WhenEditorSavesSettings()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var readerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, "ABC")
            .AddUser(readerId, builder => builder
                .SetGlobalAccessLevel(x => x.CanRead = true)));

        await UpdateAsync(
            organization.Id,
            ownerId,
            "ABC",
            "  The **Boards** tracker.  ",
            [Link("https://github.com/Laraue/Laraue.Apps.Boards", "Backend"), Link("https://github.com/Laraue/laraue-boards", "Web app")]);

        var about = await GetAboutAsync(organization.Id, readerId, "ABC");
        Assert.Equal("The **Boards** tracker.", about.Description);
        Assert.Equal(["Backend", "Web app"], about.Links.Select(x => x.Description));
        Assert.False(about.CanEdit);
    }

    [Fact]
    public async Task Update_ShouldReplaceLinks_WhenGivenTheWholeList()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org.AddSpace(ownerId, "ABC"));
        await UpdateAsync(organization.Id, ownerId, "ABC", null, [Link("https://example.com/a", "A"), Link("https://example.com/b", "B")]);

        await UpdateAsync(
            organization.Id,
            ownerId,
            "ABC",
            null,
            [Link("https://example.com/c", "C"), Link("https://example.com/a", "A changed")]);

        var links = (await GetAboutAsync(organization.Id, ownerId, "ABC")).Links;
        Assert.Equal(["https://example.com/c", "https://example.com/a"], links.Select(x => x.Url));
        Assert.Equal(["C", "A changed"], links.Select(x => x.Description));
    }

    [Fact]
    public async Task Update_ShouldKeepWhenALinkWasAdded_WhenItsUrlStays()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var editorId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, "ABC")
            .AddUser(editorId, builder => builder
                .SetGlobalAccessLevel(x =>
                {
                    x.CanRead = true;
                    x.CanUpdateSpaces = true;
                })));
        await UpdateAsync(organization.Id, ownerId, "ABC", null, [Link("https://example.com/a", "A")]);

        await UpdateAsync(organization.Id, editorId, "ABC", null, [Link("https://example.com/a", "A changed"), Link("https://example.com/b", "B")]);

        var addedBy = await testScope.Database.SpaceLinks
            .Where(x => x.Space!.OrganizationId == organization.Id)
            .OrderBy(x => x.Url)
            .Select(x => x.CreatedByUserId)
            .ToListAsync();
        Assert.Equal([ownerId, editorId], addedBy);
    }

    [Fact]
    public async Task Update_ShouldLeaveLinksAndDescription_WhenTheyAreOmitted()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org.AddSpace(ownerId, "ABC"));
        await UpdateAsync(organization.Id, ownerId, "ABC", "About", [Link("https://example.com/a", "A")]);

        await UpdateAsync(organization.Id, ownerId, "ABC", description: null, links: null);

        var about = await GetAboutAsync(organization.Id, ownerId, "ABC");
        Assert.Equal("About", about.Description);
        Assert.Single(about.Links);
    }

    [Fact]
    public async Task Update_ShouldChangeNothing_WhenALinkUrlIsListedTwice()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org.AddSpace(ownerId, "ABC"));
        await UpdateAsync(organization.Id, ownerId, "ABC", "Before", [Link("https://example.com/a", "A")]);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => UpdateAsync(
            organization.Id,
            ownerId,
            "ABC",
            "After",
            [Link("https://example.com/x", "X"), Link("https://example.com/x", "X again")],
            name: "Renamed space"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        var about = await GetAboutAsync(organization.Id, ownerId, "ABC");
        Assert.Equal("Before", about.Description);
        Assert.Equal("https://example.com/a", Assert.Single(about.Links).Url);
        var space = await testScope.Database.Spaces.AsNoTracking().SingleAsync(x => x.OrganizationId == organization.Id && x.Key == "ABC");
        Assert.NotEqual("Renamed space", space.Name);
    }

    [Fact]
    public async Task Update_ShouldAddSameUrlWithOwnDescription_WhenItIsInAnotherSpace()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddSpace(ownerId, "ABC")
            .AddSpace(ownerId, "XYZ"));
        await UpdateAsync(organization.Id, ownerId, "ABC", null, [Link("https://github.com/Laraue/ui-kit", "The code we change")]);

        await UpdateAsync(organization.Id, ownerId, "XYZ", null, [Link("https://github.com/Laraue/ui-kit", "The UI kit we use")]);

        var about = await GetAboutAsync(organization.Id, ownerId, "XYZ");
        Assert.Equal("The UI kit we use", Assert.Single(about.Links).Description);
    }

    [Fact]
    public async Task Update_ShouldReturnBadRequest_WhenALinkUrlIsNotAUrl()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org.AddSpace(ownerId, "ABC"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => UpdateAsync(organization.Id, ownerId, "ABC", null, [Link("not a url", "Docs")]));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task Update_ShouldReturnBadRequest_WhenThereAreTooManyLinks()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org.AddSpace(ownerId, "ABC"));
        var links = Enumerable.Range(0, Constraints.MaxSpaceLinksPerSpace + 1)
            .Select(i => Link($"https://example.com/{i}", "Link"))
            .ToList();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => UpdateAsync(organization.Id, ownerId, "ABC", null, links));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
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

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => GetAboutAsync(organization.Id, strangerId, "ABC"));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    private static SpaceLinkRequest Link(string url, string description)
    {
        return new SpaceLinkRequest { Url = url, Description = description };
    }

    private Task<SpaceAboutDto> GetAboutAsync(long organizationId, Guid userId, string spaceKey)
    {
        return _controller
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.GetAbout(spaceKey, CancellationToken.None));
    }

    private Task UpdateAsync(
        long organizationId,
        Guid userId,
        string spaceKey,
        string? description,
        List<SpaceLinkRequest>? links,
        string name = "Space name")
    {
        return _controller
            .WithOrganizationAuthorization(organizationId, userId)
            .Execute(x => x.Update(
                spaceKey,
                new UpdateSpaceRequest
                {
                    Name = name,
                    Color = "#ffffff",
                    NewKey = spaceKey,
                    Description = description,
                    Links = links,
                },
                CancellationToken.None));
    }
}
