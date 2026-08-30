using System.Net;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Retro.WebApiHost.Controllers;
using Laraue.Apps.Retro.WebApiServices;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class RetroControllerTests(WebApiTestHost host, RetroWebApiTestHost retroHost)
    : IClassFixture<WebApiTestHost>, IClassFixture<RetroWebApiTestHost>
{
    private readonly Proxy<RetroController> _retroController = retroHost.Controller<RetroController>(host.Services);

    [Fact]
    public async Task Create_ShouldSetCreatorAsOwnerAndParticipant_Always()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);

        var retro = await testScope.Database.Retros
            .Include(x => x.Participants)
            .Include(x => x.Sections)
            .SingleAsync(x => x.Id == data.RetroId);

        Assert.Equal(data.OwnerId, retro.OwnerId);
        Assert.Equal(
            new[] { data.OwnerId, data.ParticipantId }.Order().ToArray(),
            retro.Participants.Select(x => x.UserId).Order().ToArray());
        Assert.Equal(new[] { "Good", "Bad", "Start", "Stop", "Actions" },
            retro.Sections.OrderBy(x => x.SortOrder).Select(x => x.Name).ToArray());

        var response = await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.Get(data.RetroId));

        Assert.False(response!.CanManage);
        Assert.Equal(data.OwnerId, response.Owner.UserId);
        Assert.Contains(response.Participants, x => x.UserId == data.OwnerId);
        Assert.Contains(response.Participants, x => x.UserId == data.ParticipantId);
    }

    [Fact]
    public async Task UpdateSettings_ShouldBeForbidden_WhenCallerIsParticipant()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.UpdateSettings(
                data.RetroId,
                new UpdateRetroSettingsRequest { Phase = RetroPhase.Vote, VotesPerUser = 3 })));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task SetCardVote_ShouldBeAllowedForOwner_WhenTimerIsRunning()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        var sectionId = await testScope.Database.RetroSections
            .Where(x => x.RetroId == data.RetroId)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Id)
            .FirstAsync();

        await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.UpdateSettings(
                data.RetroId,
                new UpdateRetroSettingsRequest { Phase = RetroPhase.Vote, VotesPerUser = 3 }));
        await _retroController.Execute(x => x.SetVoteTimer(
            data.RetroId,
            new SetRetroTimerRequest { Minutes = 5 }));
        var card = await _retroController.Execute(x => x.CreateCard(
            data.RetroId,
            new CreateRetroCardRequest { SectionId = sectionId, Text = "Vote", X = 10, Y = 20 }));

        var cardId = card!.Id;
        await _retroController.Execute(x => x.SetCardVote(cardId, new SetRetroCardVoteRequest { Voted = true }));

        Assert.True(await testScope.Database.RetroCardVotes
            .AnyAsync(x => x.CardId == cardId && x.UserId == data.OwnerId));
    }

    [Fact]
    public async Task SetCardVote_ShouldFail_WhenTimerIsNotRunning()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        var sectionId = await testScope.Database.RetroSections
            .Where(x => x.RetroId == data.RetroId)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Id)
            .FirstAsync();
        var card = await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest { SectionId = sectionId, Text = "Vote", X = 10, Y = 20 }));

        await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.UpdateSettings(
                data.RetroId,
                new UpdateRetroSettingsRequest { Phase = RetroPhase.Vote, VotesPerUser = 3 }));
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.SetCardVote(card!.Id, new SetRetroCardVoteRequest { Voted = true })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task SetCardVote_ShouldFail_WhenVoteLimitReached()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        var sectionId = await testScope.Database.RetroSections
            .Where(x => x.RetroId == data.RetroId)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Id)
            .FirstAsync();

        await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.UpdateSettings(
                data.RetroId,
                new UpdateRetroSettingsRequest { Phase = RetroPhase.Vote, VotesPerUser = 1 }));
        await _retroController.Execute(x => x.SetVoteTimer(
            data.RetroId,
            new SetRetroTimerRequest { Minutes = 5 }));

        var firstCard = await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest { SectionId = sectionId, Text = "First", X = 10, Y = 20 }));
        var secondCard = await _retroController.Execute(x => x.CreateCard(
            data.RetroId,
            new CreateRetroCardRequest { SectionId = sectionId, Text = "Second", X = 30, Y = 40 }));

        await _retroController.Execute(x => x.SetCardVote(firstCard!.Id, new SetRetroCardVoteRequest { Voted = true }));
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            _retroController.Execute(x => x.SetCardVote(secondCard!.Id, new SetRetroCardVoteRequest { Voted = true })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task Get_ShouldNotSendTextOfCoveredNotes_WhenPhaseIsCollect()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        var sectionId = await FirstSectionId(testScope, data.RetroId);

        await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest { SectionId = sectionId, Text = "Secret", X = 0, Y = 0 }));

        var response = await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.Get(data.RetroId));

        var card = Assert.Single(response!.Cards);
        Assert.True(card.Hidden);
        Assert.Equal(string.Empty, card.Text);
    }

    [Fact]
    public async Task Create_ShouldCarryUnfinishedActions_WhenPreviousRetroIsFinished()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        var actionsSectionId = await testScope.Database.RetroSections
            .Where(x => x.RetroId == data.RetroId)
            .OrderByDescending(x => x.SortOrder)
            .Select(x => x.Id)
            .FirstAsync();

        await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest
                {
                    SectionId = actionsSectionId,
                    Text = "Automate the release",
                    X = 1,
                    Y = 2,
                }));
        await _retroController.Execute(x => x.Finish(data.RetroId));

        var next = await _retroController.Execute(x =>
            x.Create(new CreateRetroRequest { Name = "Next retro" }));

        var response = await _retroController.Execute(x => x.Get(next!.Id));
        var card = Assert.Single(response!.Cards);

        Assert.Equal("Automate the release", card.Text);
        Assert.True(card.Revealed);
        Assert.Equal(response.Sections[^1].Id, card.SectionId);
    }

    [Fact]
    public async Task CreateCard_ShouldFail_WhenRetroIsFinished()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        var sectionId = await FirstSectionId(testScope, data.RetroId);

        await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.Finish(data.RetroId));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest { SectionId = sectionId, Text = "Late", X = 0, Y = 0 })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    private static Task<long> FirstSectionId(WebApiTestHostScope testScope, long retroId) =>
        testScope.Database.RetroSections
            .Where(x => x.RetroId == retroId)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Id)
            .FirstAsync();

    private async Task<RetroTestData> CreateRetro(WebApiTestHostScope testScope)
    {
        var ownerId = await testScope.CreateUser();
        var participantId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, setup => setup.AddUser(participantId));
        var response = await _retroController
            .WithOrganizationAuthorization(organization.Id, ownerId)
            .Execute(x => x.Create(new CreateRetroRequest { Name = "Sprint retro" }));

        return new RetroTestData(organization.Id, response!.Id, ownerId, participantId);
    }

    private record RetroTestData(long OrganizationId, long RetroId, Guid OwnerId, Guid ParticipantId);
}
