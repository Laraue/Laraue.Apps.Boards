using System.Net;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Retro.WebApiHost.Controllers;
using Laraue.Apps.Retro.WebApiServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class RetroControllerTests(WebApiTestHost host, RetroWebApiTestHost retroHost)
    : IClassFixture<WebApiTestHost>, IClassFixture<RetroWebApiTestHost>
{
    private readonly Proxy<RetroController> _retroController = retroHost.Controller<RetroController>(host.Services);

    [Fact]
    public async Task Create_ShouldLeaveParticipantsEmpty_Always()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);

        var retro = await testScope.Database.Retros
            .Include(x => x.Participants)
            .Include(x => x.Sections)
            .SingleAsync(x => x.Id == data.RetroId);

        Assert.Equal(data.OwnerId, retro.OwnerId);
        Assert.Empty(retro.Participants);
        Assert.Equal(new[] { "Good", "Bad", "Start", "Stop", "Actions" },
            retro.Sections.OrderBy(x => x.SortOrder).Select(x => x.Name).ToArray());

        var response = await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.Get(data.RetroId));

        Assert.False(response!.CanManage);
        Assert.Equal(data.OwnerId, response.Owner.UserId);
        Assert.Empty(response.Participants);
    }

    [Fact]
    public async Task JoinRealtime_ShouldAddParticipantOnce_WhenRetroIsActive()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        using var retroScope = retroHost.Services.CreateScope();
        var service = retroScope.ServiceProvider.GetRequiredService<IRetrosService>();
        var authData = new OrganizationAuthData
        {
            OrganizationId = data.OrganizationId,
            UserId = data.ParticipantId,
        };

        await service.JoinRealtime(data.RetroId, authData, CancellationToken.None);
        await service.JoinRealtime(data.RetroId, authData, CancellationToken.None);

        var participants = await testScope.Database.RetroParticipants
            .Where(x => x.RetroId == data.RetroId)
            .Select(x => x.UserId)
            .ToArrayAsync();
        Assert.Equal([data.ParticipantId], participants);
    }

    [Fact]
    public async Task JoinRealtime_ShouldNotAddParticipant_WhenRetroIsFinished()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        await SetPhase(testScope, data.RetroId, RetroPhase.Actions);
        await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.Finish(data.RetroId));
        using var retroScope = retroHost.Services.CreateScope();
        var service = retroScope.ServiceProvider.GetRequiredService<IRetrosService>();

        await service.JoinRealtime(
            data.RetroId,
            new OrganizationAuthData
            {
                OrganizationId = data.OrganizationId,
                UserId = data.ParticipantId,
            },
            CancellationToken.None);

        Assert.False(await testScope.Database.RetroParticipants.AnyAsync(x => x.RetroId == data.RetroId));
    }

    [Fact]
    public async Task AdvancePhase_ShouldBeForbidden_WhenCallerIsNotOwner()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.AdvancePhase(
                data.RetroId,
                new SetRetroPhaseRequest { Phase = RetroPhase.Group })));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task ChangePhase_ShouldAllowOnlyTheAdjacentPhaseInTheRequestedDirection()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        _retroController.WithOrganizationAuthorization(data.OrganizationId, data.OwnerId);

        var skipped = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.AdvancePhase(
                data.RetroId,
                new SetRetroPhaseRequest { Phase = RetroPhase.Vote })));
        Assert.Equal(HttpStatusCode.BadRequest, skipped.StatusCode);

        await _retroController.Execute(x => x.AdvancePhase(
            data.RetroId,
            new SetRetroPhaseRequest { Phase = RetroPhase.Group }));

        var changedThroughSettings = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.UpdateSettings(
                data.RetroId,
                new UpdateRetroSettingsRequest { Phase = RetroPhase.Vote, VotesPerUser = 3 })));
        Assert.Equal(HttpStatusCode.BadRequest, changedThroughSettings.StatusCode);

        await _retroController.Execute(x => x.RevertPhase(
            data.RetroId,
            new SetRetroPhaseRequest { Phase = RetroPhase.Collect }));

        Assert.Equal(
            RetroPhase.Collect,
            await testScope.Database.Retros
                .Where(x => x.Id == data.RetroId)
                .Select(x => x.Phase)
                .SingleAsync());
    }

    [Fact]
    public async Task Finish_ShouldFail_WhenPhaseIsNotActions()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.Finish(data.RetroId)));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
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
        var card = await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest { SectionId = sectionId, Text = "Vote", X = 10, Y = 20 }));
        await SetPhase(testScope, data.RetroId, RetroPhase.Vote);

        await _retroController.Execute(x => x.UpdateSettings(
            data.RetroId,
            new UpdateRetroSettingsRequest { Phase = RetroPhase.Vote, VotesPerUser = 3 }));
        await _retroController.Execute(x => x.SetVoteTimer(
            data.RetroId,
            new SetRetroTimerRequest { Minutes = 5 }));

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
        await SetPhase(testScope, data.RetroId, RetroPhase.Vote);

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
        var firstCard = await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest { SectionId = sectionId, Text = "First", X = 10, Y = 20 }));
        var secondCard = await _retroController.Execute(x => x.CreateCard(
            data.RetroId,
            new CreateRetroCardRequest { SectionId = sectionId, Text = "Second", X = 30, Y = 40 }));
        await SetPhase(testScope, data.RetroId, RetroPhase.Vote);

        await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.UpdateSettings(
                data.RetroId,
                new UpdateRetroSettingsRequest { Phase = RetroPhase.Vote, VotesPerUser = 1 }));
        await _retroController.Execute(x => x.SetVoteTimer(
            data.RetroId,
            new SetRetroTimerRequest { Minutes = 5 }));

        await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId).Execute(x => x.SetCardVote(firstCard!.Id, new SetRetroCardVoteRequest { Voted = true }));
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
        await SetPhase(testScope, data.RetroId, RetroPhase.Actions);
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
        await SetPhase(testScope, data.RetroId, RetroPhase.Actions);

        await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.Finish(data.RetroId));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest { SectionId = sectionId, Text = "Late", X = 0, Y = 0 })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task CreateCard_ShouldFail_WhenPhaseIsVote()
    {
        using var testScope = host.CreateTestScope();
        var frozen = await CreateFrozenRetro(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.CreateCard(
                frozen.Data.RetroId,
                new CreateRetroCardRequest { SectionId = frozen.SectionId, Text = "Late", X = 0, Y = 0 })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(1, await testScope.Database.RetroCards
            .CountAsync(x => x.Section!.RetroId == frozen.Data.RetroId));
    }

    [Fact]
    public async Task UpdateCard_ShouldKeepText_WhenPhaseIsVote()
    {
        using var testScope = host.CreateTestScope();
        var frozen = await CreateFrozenRetro(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.UpdateCard(frozen.CardId, new UpdateRetroCardRequest { Text = "Rewritten" })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(FrozenCardText, await testScope.Database.RetroCards
            .Where(x => x.Id == frozen.CardId)
            .Select(x => x.Text)
            .SingleAsync());
    }

    [Fact]
    public async Task MoveCard_ShouldKeepPosition_WhenPhaseIsVote()
    {
        using var testScope = host.CreateTestScope();
        var frozen = await CreateFrozenRetro(testScope);
        var otherSectionId = await testScope.Database.RetroSections
            .Where(x => x.RetroId == frozen.Data.RetroId && x.Id != frozen.SectionId)
            .Select(x => x.Id)
            .FirstAsync();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.MoveCard(
                frozen.CardId,
                new MoveRetroCardRequest { SectionId = otherSectionId, X = 99, Y = 99 })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(frozen.SectionId, await testScope.Database.RetroCards
            .Where(x => x.Id == frozen.CardId)
            .Select(x => x.SectionId)
            .SingleAsync());
    }

    [Fact]
    public async Task DeleteCard_ShouldKeepCard_WhenPhaseIsVote()
    {
        using var testScope = host.CreateTestScope();
        var frozen = await CreateFrozenRetro(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.DeleteCard(frozen.CardId)));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.True(await testScope.Database.RetroCards.AnyAsync(x => x.Id == frozen.CardId));
    }

    [Fact]
    public async Task SetCardRevealed_ShouldFail_WhenPhaseIsVote()
    {
        using var testScope = host.CreateTestScope();
        var frozen = await CreateFrozenRetro(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.SetCardRevealed(
                frozen.CardId,
                new SetRetroCardRevealedRequest { Revealed = true })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task SetMyCardsRevealed_ShouldFail_WhenPhaseIsVote()
    {
        using var testScope = host.CreateTestScope();
        var frozen = await CreateFrozenRetro(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.SetMyCardsRevealed(
                frozen.Data.RetroId,
                new SetRetroRevealRequest { Revealed = true })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task CreateCard_ShouldSucceed_WhenOwnerRevertedPhaseBackToGroup()
    {
        using var testScope = host.CreateTestScope();
        var frozen = await CreateFrozenRetro(testScope);

        await _retroController.Execute(x => x.RevertPhase(
            frozen.Data.RetroId,
            new SetRetroPhaseRequest { Phase = RetroPhase.Group }));
        await _retroController.Execute(x => x.CreateCard(
            frozen.Data.RetroId,
            new CreateRetroCardRequest { SectionId = frozen.SectionId, Text = "One more", X = 0, Y = 0 }));

        Assert.Equal(2, await testScope.Database.RetroCards
            .CountAsync(x => x.Section!.RetroId == frozen.Data.RetroId));
    }

    [Fact]
    public async Task Get_ShouldHideVoteTotals_WhenPhaseIsVote()
    {
        using var testScope = host.CreateTestScope();
        var frozen = await CreateFrozenRetro(testScope);
        await _retroController.Execute(x => x.SetVoteTimer(
            frozen.Data.RetroId,
            new SetRetroTimerRequest { Minutes = 5 }));
        await _retroController.Execute(x => x.SetCardVote(
            frozen.CardId,
            new SetRetroCardVoteRequest { Voted = true }));

        var response = await _retroController
            .WithOrganizationAuthorization(frozen.Data.OrganizationId, frozen.Data.ParticipantId)
            .Execute(x => x.Get(frozen.Data.RetroId));

        var card = Assert.Single(response!.Cards);
        Assert.Equal(0, card.Votes);
        Assert.False(card.VotedByMe);
        Assert.Equal(0, response.MyVotes);
    }

    [Fact]
    public async Task Get_ShouldKeepOwnChoiceAndRemainingLimit_WhenPhaseIsVote()
    {
        using var testScope = host.CreateTestScope();
        var frozen = await CreateFrozenRetro(testScope);
        await _retroController.Execute(x => x.SetVoteTimer(
            frozen.Data.RetroId,
            new SetRetroTimerRequest { Minutes = 5 }));
        await _retroController.Execute(x => x.SetCardVote(
            frozen.CardId,
            new SetRetroCardVoteRequest { Voted = true }));

        var response = await _retroController.Execute(x => x.Get(frozen.Data.RetroId));

        var card = Assert.Single(response!.Cards);
        Assert.True(card.VotedByMe);
        Assert.Equal(0, card.Votes);
        Assert.Equal(1, response.MyVotes);
        Assert.Equal(3, response.VotesPerUser);
    }

    [Fact]
    public async Task Get_ShouldRevealVoteTotals_WhenPhaseAdvancedToDiscuss()
    {
        using var testScope = host.CreateTestScope();
        var frozen = await CreateFrozenRetro(testScope);
        await _retroController.Execute(x => x.SetVoteTimer(
            frozen.Data.RetroId,
            new SetRetroTimerRequest { Minutes = 5 }));
        await _retroController.Execute(x => x.SetCardVote(
            frozen.CardId,
            new SetRetroCardVoteRequest { Voted = true }));

        await _retroController.Execute(x => x.AdvancePhase(
            frozen.Data.RetroId,
            new SetRetroPhaseRequest { Phase = RetroPhase.Discuss }));
        var response = await _retroController
            .WithOrganizationAuthorization(frozen.Data.OrganizationId, frozen.Data.ParticipantId)
            .Execute(x => x.Get(frozen.Data.RetroId));

        Assert.Equal(1, Assert.Single(response!.Cards).Votes);
    }

    private const string FrozenCardText = "Topic";

    /// <summary>A retro in Vote with one card of the owner created back in Collect.</summary>
    private async Task<FrozenRetro> CreateFrozenRetro(WebApiTestHostScope testScope)
    {
        var data = await CreateRetro(testScope);
        var sectionId = await FirstSectionId(testScope, data.RetroId);
        var card = await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest { SectionId = sectionId, Text = FrozenCardText, X = 0, Y = 0 }));
        await SetPhase(testScope, data.RetroId, RetroPhase.Vote);

        return new FrozenRetro(data, sectionId, card!.Id);
    }

    private record FrozenRetro(RetroTestData Data, long SectionId, Guid CardId);

    private static Task<long> FirstSectionId(WebApiTestHostScope testScope, long retroId) =>
        testScope.Database.RetroSections
            .Where(x => x.RetroId == retroId)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Id)
            .FirstAsync();

    private static Task SetPhase(
        WebApiTestHostScope testScope,
        long retroId,
        RetroPhase phase) =>
        testScope.Database.Retros
            .Where(x => x.Id == retroId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Phase, phase));

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
