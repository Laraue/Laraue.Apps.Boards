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
        await _retroController.Execute(x => x.SetPhaseTimer(
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
        await _retroController.Execute(x => x.SetPhaseTimer(
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
    public async Task Create_ShouldCarryOpenActions_WhenBasedOnAnotherRetro()
    {
        using var testScope = host.CreateTestScope();
        var previous = await CreateAction(testScope);
        await _retroController.Execute(x => x.SetCardAssignee(
            previous.CardId,
            new SetRetroCardAssigneeRequest { AssigneeId = previous.Retro.ParticipantId }));
        await _retroController.Execute(x => x.Finish(previous.Retro.RetroId));

        var next = await _retroController.Execute(x => x.Create(new CreateRetroRequest
        {
            Name = "Next retro",
            BasedOnRetroId = previous.Retro.RetroId,
        }));

        var response = await _retroController.Execute(x => x.Get(next!.Id));
        var card = Assert.Single(response!.Cards);

        Assert.Equal("Automate the release", card.Text);
        Assert.True(card.Revealed);
        Assert.Equal(response.Sections[^1].Id, card.SectionId);
        // The owner travels with the action, so nobody has to assign it again.
        Assert.Equal(previous.Retro.ParticipantId, card.Assignee!.UserId);
    }

    [Fact]
    public async Task Create_ShouldCarryNothing_WhenNoRetroIsChosenToBuildOn()
    {
        using var testScope = host.CreateTestScope();
        var previous = await CreateAction(testScope);
        await _retroController.Execute(x => x.Finish(previous.Retro.RetroId));

        var next = await _retroController.Execute(x =>
            x.Create(new CreateRetroRequest { Name = "Unrelated retro" }));

        var response = await _retroController.Execute(x => x.Get(next!.Id));
        Assert.Empty(response!.Cards);
    }

    [Fact]
    public async Task Create_ShouldSkipDoneActions_WhenBasedOnAnotherRetro()
    {
        using var testScope = host.CreateTestScope();
        var previous = await CreateAction(testScope);
        await _retroController.Execute(x => x.SetCardDone(
            previous.CardId,
            new SetRetroCardDoneRequest { Done = true }));

        var next = await _retroController.Execute(x => x.Create(new CreateRetroRequest
        {
            Name = "Next retro",
            BasedOnRetroId = previous.Retro.RetroId,
        }));

        var response = await _retroController.Execute(x => x.Get(next!.Id));
        Assert.Empty(response!.Cards);
    }

    [Fact]
    public async Task Create_ShouldFail_WhenBasedOnARetroOfAnotherOrganization()
    {
        using var testScope = host.CreateTestScope();
        var theirs = await CreateRetro(testScope);
        var mine = await CreateRetro(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .WithOrganizationAuthorization(mine.OrganizationId, mine.OwnerId)
            .Execute(x => x.Create(new CreateRetroRequest
            {
                Name = "Next retro",
                BasedOnRetroId = theirs.RetroId,
            })));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task Get_ShouldReportOpenActions_ForEveryRetroOfTheOrganization()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateAction(testScope);

        var listed = await _retroController.Execute(x => x.Get());
        Assert.Equal(1, Assert.Single(listed!).OpenActionCount);

        await _retroController.Execute(x => x.SetCardDone(
            data.CardId,
            new SetRetroCardDoneRequest { Done = true }));

        var closed = await _retroController.Execute(x => x.Get());
        Assert.Equal(0, Assert.Single(closed!).OpenActionCount);
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
    public async Task MoveCard_ShouldRearrange_ButKeepActionsClosed_WhenPhaseIsVote()
    {
        using var testScope = host.CreateTestScope();
        var frozen = await CreateFrozenRetro(testScope);
        var sections = await testScope.Database.RetroSections
            .Where(x => x.RetroId == frozen.Data.RetroId)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Id)
            .ToListAsync();
        var otherSectionId = sections[^2];
        var actionsSectionId = sections[^1];

        // Sliding a note around is layout, not content, so a frozen board can still be tidied up.
        await _retroController.Execute(x => x.MoveCard(
            frozen.CardId,
            new MoveRetroCardRequest { SectionId = otherSectionId, X = 99, Y = 99 }));

        Assert.Equal(otherSectionId, await testScope.Database.RetroCards
            .Where(x => x.Id == frozen.CardId)
            .Select(x => x.SectionId)
            .SingleAsync());

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.MoveCard(
                frozen.CardId,
                new MoveRetroCardRequest { SectionId = actionsSectionId, X = 5, Y = 5 })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(otherSectionId, await testScope.Database.RetroCards
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
        await _retroController.Execute(x => x.SetPhaseTimer(
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
        await _retroController.Execute(x => x.SetPhaseTimer(
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
        await _retroController.Execute(x => x.SetPhaseTimer(
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

    [Fact]
    public async Task CreateCard_ShouldFail_WhenActionIsAddedOutsideActionsPhase()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        var actionsSectionId = await ActionsSectionId(testScope, data.RetroId);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest
                {
                    SectionId = actionsSectionId,
                    Text = "Too early",
                    X = 0,
                    Y = 0,
                })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task CreateCard_ShouldFail_WhenTopicIsAddedDuringActionsPhase()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        var sectionId = await FirstSectionId(testScope, data.RetroId);
        await SetPhase(testScope, data.RetroId, RetroPhase.Actions);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest { SectionId = sectionId, Text = "Too late", X = 0, Y = 0 })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task MoveCard_ShouldTurnTopicIntoAction_WhenMovedToActionsDuringActionsPhase()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        var sectionId = await FirstSectionId(testScope, data.RetroId);
        var actionsSectionId = await ActionsSectionId(testScope, data.RetroId);
        var card = await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest { SectionId = sectionId, Text = "Painful releases", X = 0, Y = 0 }));
        await SetPhase(testScope, data.RetroId, RetroPhase.Actions);

        await _retroController.Execute(x => x.MoveCard(
            card!.Id,
            new MoveRetroCardRequest { SectionId = actionsSectionId, X = 5, Y = 5 }));

        Assert.Equal(actionsSectionId, await testScope.Database.RetroCards
            .Where(x => x.Id == card.Id)
            .Select(x => x.SectionId)
            .SingleAsync());
    }

    [Fact]
    public async Task SetCardAssignee_ShouldStoreParticipant_WhenPhaseIsActions()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateAction(testScope);

        await _retroController.Execute(x => x.SetCardAssignee(
            data.CardId,
            new SetRetroCardAssigneeRequest { AssigneeId = data.Retro.ParticipantId }));

        var response = await _retroController.Execute(x => x.Get(data.Retro.RetroId));
        var card = Assert.Single(response!.Cards);
        Assert.Equal(data.Retro.ParticipantId, card.Assignee!.UserId);

        await _retroController.Execute(x => x.SetCardAssignee(
            data.CardId,
            new SetRetroCardAssigneeRequest { AssigneeId = null }));

        var cleared = await _retroController.Execute(x => x.Get(data.Retro.RetroId));
        Assert.Null(Assert.Single(cleared!.Cards).Assignee);
    }

    [Fact]
    public async Task SetCardAssignee_ShouldFail_WhenUserIsNotAParticipant()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateAction(testScope);
        var outsiderId = await testScope.CreateUser();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.SetCardAssignee(
                data.CardId,
                new SetRetroCardAssigneeRequest { AssigneeId = outsiderId })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task SetCardAssignee_ShouldFail_WhenCardIsNotAnAction()
    {
        using var testScope = host.CreateTestScope();
        var frozen = await CreateFrozenRetro(testScope);
        await SetPhase(testScope, frozen.Data.RetroId, RetroPhase.Actions);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.SetCardAssignee(
                frozen.CardId,
                new SetRetroCardAssigneeRequest { AssigneeId = frozen.Data.OwnerId })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task SetCardAssignee_ShouldFail_WhenRetroIsFinished()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateAction(testScope);
        await _retroController.Execute(x => x.Finish(data.Retro.RetroId));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.SetCardAssignee(
                data.CardId,
                new SetRetroCardAssigneeRequest { AssigneeId = data.Retro.ParticipantId })));

        // A finished retro drops out of the editable-card query entirely, like every other change.
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task Get_ShouldReturnCardsInStackOrder_WhenACardWasMoved()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        var sectionId = await FirstSectionId(testScope, data.RetroId);
        _retroController.WithOrganizationAuthorization(data.OrganizationId, data.OwnerId);

        var first = await _retroController.Execute(x => x.CreateCard(
            data.RetroId,
            new CreateRetroCardRequest { SectionId = sectionId, Text = "First", X = 0, Y = 0 }));
        var second = await _retroController.Execute(x => x.CreateCard(
            data.RetroId,
            new CreateRetroCardRequest { SectionId = sectionId, Text = "Second", X = 10, Y = 10 }));

        var created = await _retroController.Execute(x => x.Get(data.RetroId));
        Assert.Equal(["First", "Second"], created!.Cards.Select(x => x.Text));

        await _retroController.Execute(x => x.MoveCard(
            first!.Id,
            new MoveRetroCardRequest { SectionId = sectionId, X = 20, Y = 20 }));

        var moved = await _retroController.Execute(x => x.Get(data.RetroId));
        Assert.Equal(["Second", "First"], moved!.Cards.Select(x => x.Text));
        Assert.True(moved.Cards[^1].StackOrder > moved.Cards[0].StackOrder);
        Assert.Equal(second!.Id, moved.Cards[0].Id);
    }

    [Fact]
    public async Task Get_ShouldKeepStackOrder_WhenSomebodyElseReloadsTheBoard()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        var sectionId = await FirstSectionId(testScope, data.RetroId);

        var first = await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest { SectionId = sectionId, Text = "First", X = 0, Y = 0 }));
        await _retroController.Execute(x => x.CreateCard(
            data.RetroId,
            new CreateRetroCardRequest { SectionId = sectionId, Text = "Second", X = 10, Y = 10 }));
        await _retroController.Execute(x => x.MoveCard(
            first!.Id,
            new MoveRetroCardRequest { SectionId = sectionId, X = 20, Y = 20 }));

        var mine = await _retroController.Execute(x => x.Get(data.RetroId));
        var theirs = await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.Get(data.RetroId));

        Assert.Equal(
            mine!.Cards.Select(x => x.Id),
            theirs!.Cards.Select(x => x.Id));
    }

    [Fact]
    public async Task Get_ShouldRevealResults_WhenRetroIsFinished()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        var sectionId = await FirstSectionId(testScope, data.RetroId);
        var card = await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest { SectionId = sectionId, Text = "Deploys hurt", X = 0, Y = 0 }));
        await SetPhase(testScope, data.RetroId, RetroPhase.Vote);
        await _retroController.Execute(x => x.SetPhaseTimer(
            data.RetroId,
            new SetRetroTimerRequest { Minutes = 5 }));
        await _retroController.Execute(x => x.SetCardVote(
            card!.Id,
            new SetRetroCardVoteRequest { Voted = true }));

        // A retro finished before the phase workflow existed is still parked in Collect, where
        // results and covered notes would otherwise stay hidden forever.
        await testScope.Database.Retros
            .Where(x => x.Id == data.RetroId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.Phase, RetroPhase.Collect)
                .SetProperty(p => p.FinishedAt, DateTime.UtcNow));

        var response = await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.Get(data.RetroId));

        var seen = Assert.Single(response!.Cards);
        Assert.Equal(1, seen.Votes);
        Assert.False(seen.Hidden);
        Assert.Equal("Deploys hurt", seen.Text);
    }

    [Fact]
    public async Task TransferOwnership_ShouldMoveControlToAnotherParticipant()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        testScope.Database.RetroParticipants.Add(
            new RetroParticipant { RetroId = data.RetroId, UserId = data.ParticipantId });
        await testScope.Database.SaveChangesAsync();

        await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.TransferOwnership(
                data.RetroId,
                new TransferRetroOwnershipRequest { UserId = data.ParticipantId }));

        var theirs = await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.Get(data.RetroId));
        Assert.True(theirs!.CanManage);

        // The retro runs on without its creator: the new facilitator drives the phases...
        await _retroController.Execute(x => x.AdvancePhase(
            data.RetroId,
            new SetRetroPhaseRequest { Phase = RetroPhase.Group }));

        // ...and the previous one is left with no control at all.
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.AdvancePhase(
                data.RetroId,
                new SetRetroPhaseRequest { Phase = RetroPhase.Vote })));
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);

        var mine = await _retroController.Execute(x => x.Get(data.RetroId));
        Assert.False(mine!.CanManage);
        Assert.Equal(data.ParticipantId, mine.Owner.UserId);
    }

    [Fact]
    public async Task TransferOwnership_ShouldFail_WhenTargetNeverJoinedTheRetro()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.TransferOwnership(
                data.RetroId,
                new TransferRetroOwnershipRequest { UserId = data.ParticipantId })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task TransferOwnership_ShouldBeForbidden_WhenCallerIsNotTheFacilitator()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.TransferOwnership(
                data.RetroId,
                new TransferRetroOwnershipRequest { UserId = data.ParticipantId })));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task Rename_ShouldReplaceTheDefaultDateName()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);

        await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.Rename(data.RetroId, new RenameRetroRequest { Name = "  Sprint 42  " }));

        var response = await _retroController.Execute(x => x.Get(data.RetroId));
        Assert.Equal("Sprint 42", response!.Name);

        var blank = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.Rename(data.RetroId, new RenameRetroRequest { Name = "   " })));
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
    }

    [Fact]
    public async Task Rename_ShouldBeForbidden_WhenCallerIsNotTheFacilitator()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.Rename(data.RetroId, new RenameRetroRequest { Name = "Theirs" })));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task Delete_ShouldRemoveTheRetroWithItsBoard_EvenWhenFinished()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateAction(testScope);
        await _retroController.Execute(x => x.Finish(data.Retro.RetroId));

        await _retroController.Execute(x => x.Delete(data.Retro.RetroId));

        Assert.False(await testScope.Database.Retros.AnyAsync(x => x.Id == data.Retro.RetroId));
        Assert.False(await testScope.Database.RetroCards.AnyAsync(x => x.Id == data.CardId));
        Assert.False(await testScope.Database.RetroSections
            .AnyAsync(x => x.RetroId == data.Retro.RetroId));
        Assert.False(await testScope.Database.RetroParticipants
            .AnyAsync(x => x.RetroId == data.Retro.RetroId));
    }

    [Fact]
    public async Task Delete_ShouldBeForbidden_WhenCallerIsNotTheFacilitator()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.Delete(data.RetroId)));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.True(await testScope.Database.Retros.AnyAsync(x => x.Id == data.RetroId));
    }

    /// <summary>A retro in Actions with one action card and both users joined as participants.</summary>
    private async Task<ActionTestData> CreateAction(WebApiTestHostScope testScope)
    {
        var data = await CreateRetro(testScope);
        var actionsSectionId = await ActionsSectionId(testScope, data.RetroId);
        testScope.Database.RetroParticipants.AddRange(
            new RetroParticipant { RetroId = data.RetroId, UserId = data.OwnerId },
            new RetroParticipant { RetroId = data.RetroId, UserId = data.ParticipantId });
        await testScope.Database.SaveChangesAsync();
        await SetPhase(testScope, data.RetroId, RetroPhase.Actions);

        var card = await _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest
                {
                    SectionId = actionsSectionId,
                    Text = "Automate the release",
                    X = 0,
                    Y = 0,
                }));

        return new ActionTestData(data, card!.Id);
    }

    private record ActionTestData(RetroTestData Retro, Guid CardId);

    private static Task<long> ActionsSectionId(WebApiTestHostScope testScope, long retroId) =>
        testScope.Database.RetroSections
            .Where(x => x.RetroId == retroId)
            .OrderByDescending(x => x.SortOrder)
            .Select(x => x.Id)
            .FirstAsync();

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

    [Fact]
    public async Task SetPhaseTimer_ShouldRunInEveryPhase_AndSurviveAReload()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        _retroController.WithOrganizationAuthorization(data.OrganizationId, data.OwnerId);

        await _retroController.Execute(x => x.SetPhaseTimer(
            data.RetroId,
            new SetRetroTimerRequest { Minutes = 5 }));

        var collect = await _retroController.Execute(x => x.Get(data.RetroId));
        Assert.NotNull(collect!.PhaseEndsAt);

        // Everyone reads the deadline from the server, so a reload shows the same countdown.
        var reloaded = await _retroController.Execute(x => x.Get(data.RetroId));
        Assert.Equal(collect.PhaseEndsAt, reloaded!.PhaseEndsAt);

        await _retroController.Execute(x => x.SetPhaseTimer(
            data.RetroId,
            new SetRetroTimerRequest { Minutes = null }));

        var stopped = await _retroController.Execute(x => x.Get(data.RetroId));
        Assert.Null(stopped!.PhaseEndsAt);
    }

    [Fact]
    public async Task SetPhaseTimer_ShouldBeForbidden_WhenCallerIsNotOwner()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.ParticipantId)
            .Execute(x => x.SetPhaseTimer(data.RetroId, new SetRetroTimerRequest { Minutes = 5 })));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task AdvancePhase_ShouldStopTheRunningTimer()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        _retroController.WithOrganizationAuthorization(data.OrganizationId, data.OwnerId);
        await _retroController.Execute(x => x.SetPhaseTimer(
            data.RetroId,
            new SetRetroTimerRequest { Minutes = 5 }));

        await _retroController.Execute(x => x.AdvancePhase(
            data.RetroId,
            new SetRetroPhaseRequest { Phase = RetroPhase.Group }));

        var response = await _retroController.Execute(x => x.Get(data.RetroId));
        Assert.Null(response!.PhaseEndsAt);
    }

    [Fact]
    public async Task SetDiscussedCard_ShouldResetTheTimer_WhenTheTeamMovesToTheNextTopic()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        var sectionId = await FirstSectionId(testScope, data.RetroId);
        _retroController.WithOrganizationAuthorization(data.OrganizationId, data.OwnerId);

        var first = await _retroController.Execute(x => x.CreateCard(
            data.RetroId,
            new CreateRetroCardRequest { SectionId = sectionId, Text = "First", X = 0, Y = 0 }));
        var second = await _retroController.Execute(x => x.CreateCard(
            data.RetroId,
            new CreateRetroCardRequest { SectionId = sectionId, Text = "Second", X = 10, Y = 10 }));
        await SetPhase(testScope, data.RetroId, RetroPhase.Discuss);

        await _retroController.Execute(x => x.SetDiscussedCard(
            data.RetroId,
            new SetRetroDiscussedCardRequest { CardId = first!.Id }));
        await _retroController.Execute(x => x.SetPhaseTimer(
            data.RetroId,
            new SetRetroTimerRequest { Minutes = 5 }));

        var discussing = await _retroController.Execute(x => x.Get(data.RetroId));
        Assert.Equal(first.Id, discussing!.DiscussedCardId);
        Assert.NotNull(discussing.PhaseEndsAt);

        await _retroController.Execute(x => x.SetDiscussedCard(
            data.RetroId,
            new SetRetroDiscussedCardRequest { CardId = second!.Id }));

        var next = await _retroController.Execute(x => x.Get(data.RetroId));
        Assert.Equal(second.Id, next!.DiscussedCardId);
        Assert.Null(next.PhaseEndsAt);
    }

    [Fact]
    public async Task SetDiscussedCard_ShouldFail_WhenCardBelongsToAnotherRetro()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateRetro(testScope);
        var other = await CreateRetro(testScope);
        var otherSectionId = await FirstSectionId(testScope, other.RetroId);
        var foreignCard = await _retroController
            .WithOrganizationAuthorization(other.OrganizationId, other.OwnerId)
            .Execute(x => x.CreateCard(
                other.RetroId,
                new CreateRetroCardRequest { SectionId = otherSectionId, Text = "Theirs", X = 0, Y = 0 }));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .WithOrganizationAuthorization(data.OrganizationId, data.OwnerId)
            .Execute(x => x.SetDiscussedCard(
                data.RetroId,
                new SetRetroDiscussedCardRequest { CardId = foreignCard!.Id })));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task GroupCards_ShouldMergeNotesIntoOneTopic_WhenPhaseIsGroup()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateTopics(testScope);

        var group = await _retroController.Execute(x => x.GroupCards(
            data.Retro.RetroId,
            new GroupRetroCardsRequest { CardIds = new[] { data.FirstId, data.SecondId } }));

        var response = await _retroController.Execute(x => x.Get(data.Retro.RetroId));
        var topic = Assert.Single(response!.Groups);

        Assert.Equal(group!.Id, topic.Id);
        Assert.Equal(string.Empty, topic.Title);
        // Guid order is not creation order, so both sides get sorted.
        Assert.Equal(new[] { data.FirstId, data.SecondId }.Order(), topic.CardIds.Order());
        // The notes themselves are untouched - text, author and all.
        Assert.Equal(["First", "Second", "Third"], response.Cards.Select(x => x.Text).Order());
        Assert.Equal(
            new[] { data.FirstId, data.SecondId }.Order(),
            response.Cards.Where(x => x.GroupId == topic.Id).Select(x => x.Id).Order());
    }

    [Fact]
    public async Task GroupCards_ShouldFail_WhenFewerThanTwoNotesAreSelected()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateTopics(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.GroupCards(
                data.Retro.RetroId,
                new GroupRetroCardsRequest { CardIds = new[] { data.FirstId } })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task GroupCards_ShouldBeForbidden_WhenCallerIsNotTheFacilitator()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateTopics(testScope);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .WithOrganizationAuthorization(data.Retro.OrganizationId, data.Retro.ParticipantId)
            .Execute(x => x.GroupCards(
                data.Retro.RetroId,
                new GroupRetroCardsRequest { CardIds = new[] { data.FirstId, data.SecondId } })));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task GroupCards_ShouldFail_WhenVotingHasStarted()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateTopics(testScope);
        await SetPhase(testScope, data.Retro.RetroId, RetroPhase.Vote);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _retroController
            .Execute(x => x.GroupCards(
                data.Retro.RetroId,
                new GroupRetroCardsRequest { CardIds = new[] { data.FirstId, data.SecondId } })));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task MoveGroup_ShouldKeepCardOffsets_AndCarryTheTopicIntoTheSection()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateTopics(testScope);
        var sectionId = await FirstSectionId(testScope, data.Retro.RetroId);
        await _retroController.Execute(x => x.MoveCard(
            data.SecondId,
            new MoveRetroCardRequest { SectionId = sectionId, X = 20, Y = 30 }));
        var group = await _retroController.Execute(x => x.GroupCards(
            data.Retro.RetroId,
            new GroupRetroCardsRequest { CardIds = new[] { data.FirstId, data.SecondId } }));

        var otherSectionId = await testScope.Database.RetroSections
            .Where(x => x.RetroId == data.Retro.RetroId && x.Id != sectionId)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Id)
            .FirstAsync();

        await _retroController.Execute(x => x.MoveGroup(
            data.Retro.RetroId,
            group!.Id,
            new MoveRetroGroupRequest { DeltaX = 10, DeltaY = 15, SectionId = otherSectionId }));

        var response = await _retroController.Execute(x => x.Get(data.Retro.RetroId));
        var first = response!.Cards.Single(x => x.Id == data.FirstId);
        var second = response.Cards.Single(x => x.Id == data.SecondId);
        var third = response.Cards.Single(x => x.Id == data.ThirdId);

        Assert.Equal((10, 15), (first.X, first.Y));
        Assert.Equal((30, 45), (second.X, second.Y));
        Assert.Equal((0, 0), (third.X, third.Y));
        Assert.True(first.StackOrder < second.StackOrder);
        // The whole topic belongs to the section it was dropped on, colour and all.
        Assert.Equal(otherSectionId, first.SectionId);
        Assert.Equal(otherSectionId, second.SectionId);
    }

    [Fact]
    public async Task Ungroup_ShouldKeepTheNotes_AndFailAfterVotingStarted()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateTopics(testScope);
        var group = await _retroController.Execute(x => x.GroupCards(
            data.Retro.RetroId,
            new GroupRetroCardsRequest { CardIds = new[] { data.FirstId, data.SecondId } }));

        await _retroController.Execute(x => x.Ungroup(data.Retro.RetroId, group!.Id));

        var response = await _retroController.Execute(x => x.Get(data.Retro.RetroId));
        Assert.Empty(response!.Groups);
        Assert.Equal(3, response.Cards.Length);
        Assert.All(response.Cards, card => Assert.Null(card.GroupId));

        var regrouped = await _retroController.Execute(x => x.GroupCards(
            data.Retro.RetroId,
            new GroupRetroCardsRequest { CardIds = new[] { data.FirstId, data.SecondId } }));
        await SetPhase(testScope, data.Retro.RetroId, RetroPhase.Vote);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            _retroController.Execute(x => x.Ungroup(data.Retro.RetroId, regrouped!.Id)));
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task SetGroupTitle_ShouldStoreTheHeadline()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateTopics(testScope);
        var group = await _retroController.Execute(x => x.GroupCards(
            data.Retro.RetroId,
            new GroupRetroCardsRequest { CardIds = new[] { data.FirstId, data.SecondId } }));

        await _retroController.Execute(x => x.SetGroupTitle(
            data.Retro.RetroId,
            group!.Id,
            new SetRetroGroupTitleRequest { Title = "  Painful releases  " }));

        var response = await _retroController.Execute(x => x.Get(data.Retro.RetroId));
        Assert.Equal("Painful releases", Assert.Single(response!.Groups).Title);
    }

    [Fact]
    public async Task SetVote_ShouldCountOncePerGroup_WhateverNoteIsVotedFor()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateTopics(testScope);
        var group = await _retroController.Execute(x => x.GroupCards(
            data.Retro.RetroId,
            new GroupRetroCardsRequest { CardIds = new[] { data.FirstId, data.SecondId } }));
        await SetPhase(testScope, data.Retro.RetroId, RetroPhase.Vote);
        await _retroController.Execute(x => x.SetPhaseTimer(
            data.Retro.RetroId,
            new SetRetroTimerRequest { Minutes = 5 }));

        await _retroController.Execute(x => x.SetCardVote(
            data.FirstId,
            new SetRetroCardVoteRequest { Voted = true }));
        await _retroController.Execute(x => x.SetCardVote(
            data.SecondId,
            new SetRetroCardVoteRequest { Voted = true }));

        await SetPhase(testScope, data.Retro.RetroId, RetroPhase.Discuss);
        var response = await _retroController.Execute(x => x.Get(data.Retro.RetroId));
        var topic = Assert.Single(response!.Groups);

        Assert.Equal(group!.Id, topic.Id);
        Assert.Equal(1, topic.Votes);
        Assert.True(topic.VotedByMe);
        Assert.Equal(1, response.MyVotes);
    }

    [Fact]
    public async Task SetVote_ShouldTakeTheVoteBack_WhenItWasCastBeforeGrouping()
    {
        using var testScope = host.CreateTestScope();
        var data = await CreateTopics(testScope);
        await SetPhase(testScope, data.Retro.RetroId, RetroPhase.Vote);
        await _retroController.Execute(x => x.SetPhaseTimer(
            data.Retro.RetroId,
            new SetRetroTimerRequest { Minutes = 5 }));
        await _retroController.Execute(x => x.SetCardVote(
            data.SecondId,
            new SetRetroCardVoteRequest { Voted = true }));

        await SetPhase(testScope, data.Retro.RetroId, RetroPhase.Group);
        var group = await _retroController.Execute(x => x.GroupCards(
            data.Retro.RetroId,
            new GroupRetroCardsRequest { CardIds = new[] { data.FirstId, data.SecondId } }));
        await SetPhase(testScope, data.Retro.RetroId, RetroPhase.Vote);

        // The vote sits on the second note, new votes would land on the first - the topic still
        // has to give the vote back.
        await _retroController.Execute(x => x.SetCardVote(
            data.FirstId,
            new SetRetroCardVoteRequest { Voted = false }));

        await SetPhase(testScope, data.Retro.RetroId, RetroPhase.Discuss);
        var response = await _retroController.Execute(x => x.Get(data.Retro.RetroId));
        var topic = Assert.Single(response!.Groups, x => x.Id == group!.Id);

        Assert.Equal(0, topic.Votes);
        Assert.False(topic.VotedByMe);
        Assert.Equal(0, response.MyVotes);
    }

    /// <summary>A retro in Group with three topic notes of the facilitator.</summary>
    private async Task<TopicsTestData> CreateTopics(WebApiTestHostScope testScope)
    {
        var data = await CreateRetro(testScope);
        var sectionId = await FirstSectionId(testScope, data.RetroId);
        _retroController.WithOrganizationAuthorization(data.OrganizationId, data.OwnerId);

        var ids = new List<Guid>();
        foreach (var text in new[] { "First", "Second", "Third" })
        {
            var card = await _retroController.Execute(x => x.CreateCard(
                data.RetroId,
                new CreateRetroCardRequest { SectionId = sectionId, Text = text, X = 0, Y = 0 }));
            ids.Add(card!.Id);
        }

        await SetPhase(testScope, data.RetroId, RetroPhase.Group);

        return new TopicsTestData(data, ids[0], ids[1], ids[2]);
    }

    private record TopicsTestData(RetroTestData Retro, Guid FirstId, Guid SecondId, Guid ThirdId);

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
