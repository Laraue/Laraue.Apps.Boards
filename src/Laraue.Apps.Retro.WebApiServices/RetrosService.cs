using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.Exceptions.Web;
using Laraue.Apps.Retro.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ErrorMessages = Laraue.Apps.Retro.WebApiServices.Resources.ErrorMessages;
// "Retro" (the entity) collides with the "Laraue.Apps.Retro" namespace segment this project
// lives under - alias it so the bare type name resolves correctly.
using RetroEntity = Laraue.Apps.Boards.DataAccess.Models.Retro;

namespace Laraue.Apps.Retro.WebApiServices;

public interface IRetrosService
{
    Task<RetroListItem[]> Get(OrganizationAuthData authData, CancellationToken cancellationToken);
    Task Delete(long id, OrganizationAuthData authData, CancellationToken cancellationToken);
    Task Rename(
        long id,
        RenameRetroRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task<RetroUser> JoinRealtime(
        long id,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task<GetRetroResponse> Get(long id, OrganizationAuthData authData, CancellationToken cancellationToken);
    Task TransferOwnership(
        long id,
        TransferRetroOwnershipRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task<CreateRetroResponse> Create(
        CreateRetroRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task Finish(long id, OrganizationAuthData authData, CancellationToken cancellationToken);
    Task UpdateSettings(
        long id,
        UpdateRetroSettingsRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task AdvancePhase(
        long id,
        SetRetroPhaseRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task RevertPhase(
        long id,
        SetRetroPhaseRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task SetPhaseTimer(
        long id,
        SetRetroTimerRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task SetDiscussedCard(
        long id,
        SetRetroDiscussedCardRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task SetMyCardsRevealed(
        long id,
        SetRetroRevealRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task<CreateRetroCardResponse> CreateCard(
        long id,
        CreateRetroCardRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task UpdateCard(
        Guid cardId,
        UpdateRetroCardRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task MoveCard(
        Guid cardId,
        MoveRetroCardRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task MoveGroup(
        long id,
        long groupId,
        MoveRetroGroupRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task ResetVotes(long id, OrganizationAuthData authData, CancellationToken cancellationToken);
    Task DeleteCard(Guid cardId, OrganizationAuthData authData, CancellationToken cancellationToken);
    Task SetCardVote(
        Guid cardId,
        SetRetroCardVoteRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task SetCardDone(
        Guid cardId,
        SetRetroCardDoneRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task SetCardRevealed(
        Guid cardId,
        SetRetroCardRevealedRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task SetCardAssignee(
        Guid cardId,
        SetRetroCardAssigneeRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task<GroupRetroCardsResponse> GroupCards(
        long id,
        GroupRetroCardsRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task Ungroup(
        long id,
        long groupId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task SetGroupTitle(
        long id,
        long groupId,
        SetRetroGroupTitleRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
}

public class RetrosService(
    DatabaseContext context,
    ICoreRetrosService coreRetrosService,
    IHubContext<RetroHub> retroHub) : IRetrosService
{
    public async Task<RetroUser> JoinRealtime(
        long id,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureAccessible(id, authData, cancellationToken);
        await coreRetrosService.Join(id, authData.UserId, cancellationToken);
        return await GetCurrentUser(authData, cancellationToken);
    }

    /// <summary>
    /// A retro starts named after the day it happened; the team renames it to what it was about.
    /// </summary>
    public async Task Rename(
        long id,
        RenameRetroRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureOwner(id, authData, cancellationToken);

        var name = request.Name.Trim();
        if (name.Length == 0)
            throw new BadRequestException(nameof(request.Name), ErrorMessages.RetroNameRequired);

        await coreRetrosService.Rename(id, name, cancellationToken);
        await Changed(id, cancellationToken);
    }

    /// <summary>Only the facilitator can throw a retro away, finished or not.</summary>
    public async Task Delete(
        long id,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureRetroOwner(id, authData, cancellationToken);
        await coreRetrosService.Delete(id, cancellationToken);
    }

    public async Task<RetroListItem[]> Get(
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureMember(authData, cancellationToken);

        return await context.Retros
            .Where(x => x.OrganizationId == authData.OrganizationId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new RetroListItem
            {
                Id = x.Id,
                Name = x.Name,
                CreatedAt = x.CreatedAt,
                FinishedAt = x.FinishedAt,
                CardCount = x.Sections.SelectMany(s => s.Cards).Count(),
                OpenActionCount = x.Sections
                    .OrderByDescending(s => s.SortOrder)
                    .Take(1)
                    .SelectMany(s => s.Cards)
                    .Count(c => !c.Done),
            })
            .ToArrayAsync(cancellationToken);
    }

    public async Task<GetRetroResponse> Get(
        long id,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureAccessible(id, authData, cancellationToken);
        var currentUser = await GetCurrentUser(authData, cancellationToken);

        var retro = await context.Retros
            .Where(x => x.Id == id && x.OrganizationId == authData.OrganizationId)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Color,
                x.Phase,
                x.CreatedAt,
                x.FinishedAt,
                x.VotesPerUser,
                x.PhaseEndsAt,
                x.DiscussedCardId,
                x.OwnerId,
                Owner = new RetroUser
                {
                    UserId = x.Owner!.Id,
                    DisplayName = x.Owner.DisplayName,
                    Initials = x.Owner.Initials,
                    Color = x.Owner.Color,
                    IsCurrentUser = x.Owner.Id == authData.UserId,
                },
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw RetroNotFound(id);

        var sections = await context.RetroSections
            .Where(x => x.RetroId == id)
            .OrderBy(x => x.SortOrder)
            .Select(x => new RetroSectionDto
            {
                Id = x.Id,
                Name = x.Name,
                Color = x.Color,
                SortOrder = x.SortOrder,
            })
            .ToArrayAsync(cancellationToken);

        // Totals stay hidden until the facilitator closes Vote - a client whose local timer ran
        // out must not see intermediate results, and everyone gets them at the same moment.
        // A finished retro is a record of what happened, so it always shows its results - even
        // one finished before the phase workflow existed, which is still parked in Collect.
        var finished = retro.FinishedAt.HasValue;
        var voteResultsVisible = finished || retro.Phase is RetroPhase.Discuss or RetroPhase.Actions;
        var coversNotes = !finished && retro.Phase == RetroPhase.Collect;
        var cards = await context.RetroCards
            .Where(x => x.Section!.RetroId == id)
            // Bottom of the stack first - the client paints them in exactly this order. CreatedAt
            // breaks the tie when two moves happened to land on the same order.
            .OrderBy(x => x.StackOrder)
            .ThenBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Select(c => new RetroCardDto
            {
                Id = c.Id,
                SectionId = c.SectionId,
                // A covered note must not reach the client at all - the UI only blurs it.
                Text = coversNotes && c.AuthorId != authData.UserId && !c.Revealed
                    ? string.Empty
                    : c.Text,
                X = c.X,
                Y = c.Y,
                StackOrder = c.StackOrder,
                Done = c.Done,
                Hidden = coversNotes && c.AuthorId != authData.UserId && !c.Revealed,
                Revealed = c.Revealed,
                IsMine = c.AuthorId == authData.UserId,
                GroupId = c.GroupId,
                Assignee = c.AssigneeId == null
                    ? null
                    : new RetroUser
                    {
                        UserId = c.Assignee!.Id,
                        DisplayName = c.Assignee.DisplayName,
                        Initials = c.Assignee.Initials,
                        Color = c.Assignee.Color,
                        IsCurrentUser = c.AssigneeId == authData.UserId,
                    },
                Votes = voteResultsVisible ? c.Votes.Count : 0,
                VotedByMe = c.Votes.Any(v => v.UserId == authData.UserId),
                Author = new RetroUser
                {
                    UserId = c.AuthorId,
                    DisplayName = c.Author!.DisplayName,
                    Initials = c.Author.Initials,
                    Color = c.Author.Color,
                    IsCurrentUser = c.AuthorId == authData.UserId,
                },
            })
            .ToArrayAsync(cancellationToken);

        var groups = await context.RetroCardGroups
            .Where(x => x.RetroId == id)
            .Select(x => new RetroCardGroupDto
            {
                Id = x.Id,
                Title = x.Title,
                CardIds = x.Cards.Select(c => c.Id).ToArray(),
                Votes = voteResultsVisible ? x.Cards.Sum(c => c.Votes.Count) : 0,
                VotedByMe = x.Cards.Any(c => c.Votes.Any(v => v.UserId == authData.UserId)),
            })
            .ToArrayAsync(cancellationToken);

        var participants = await context.RetroParticipants
            .Where(x => x.RetroId == id)
            .Select(p => new RetroUser
            {
                UserId = p.UserId,
                DisplayName = p.User!.DisplayName,
                Initials = p.User.Initials,
                Color = p.User.Color,
                IsCurrentUser = p.UserId == authData.UserId,
            })
            .ToArrayAsync(cancellationToken);

        return new GetRetroResponse
        {
            Id = retro.Id,
            Name = retro.Name,
            Color = retro.Color,
            Phase = retro.Phase,
            CreatedAt = retro.CreatedAt,
            FinishedAt = retro.FinishedAt,
            VotesPerUser = retro.VotesPerUser,
            MyVotes = cards.Count(x => x.VotedByMe),
            PhaseEndsAt = retro.PhaseEndsAt,
            DiscussedCardId = retro.DiscussedCardId,
            CanManage = retro.OwnerId == authData.UserId,
            Owner = retro.Owner,
            CurrentUser = currentUser,
            Participants = participants
                .OrderByDescending(x => x.IsCurrentUser)
                .ThenBy(x => x.DisplayName)
                .ToArray(),
            Sections = sections,
            Cards = cards,
            Groups = groups,
        };
    }

    public async Task<CreateRetroResponse> Create(
        CreateRetroRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureMember(authData, cancellationToken);
        if (request.BasedOnRetroId.HasValue)
            await EnsureAccessible(request.BasedOnRetroId.Value, authData, cancellationToken);

        var id = await coreRetrosService.Create(
            authData.OrganizationId,
            authData.UserId,
            request.Name,
            request.BasedOnRetroId,
            cancellationToken);
        return new CreateRetroResponse { Id = id };
    }

    public async Task TransferOwnership(
        long id,
        TransferRetroOwnershipRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureOwner(id, authData, cancellationToken);

        var isParticipant = await context.RetroParticipants.AnyAsync(
            x => x.RetroId == id && x.UserId == request.UserId,
            cancellationToken);
        if (!isParticipant)
        {
            throw new BadRequestException(
                nameof(request.UserId),
                ErrorMessages.RetroFacilitatorNotParticipant);
        }

        if (!await coreRetrosService.SetOwner(id, request.UserId, cancellationToken))
            throw new BadRequestException(nameof(id), ErrorMessages.RetroFinished);

        await Changed(id, cancellationToken);
    }

    public async Task Finish(
        long id,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureOwner(id, authData, cancellationToken);
        if (!await coreRetrosService.Finish(id, cancellationToken))
            throw new BadRequestException(nameof(id), ErrorMessages.RetroFinishUnavailable);

        await Changed(id, cancellationToken);
    }

    public async Task UpdateSettings(
        long id,
        UpdateRetroSettingsRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureOwner(id, authData, cancellationToken);
        if (!await coreRetrosService.UpdateSettings(
            id,
            request.Phase,
            request.VotesPerUser,
            cancellationToken))
        {
            throw new BadRequestException(nameof(request.Phase), ErrorMessages.RetroPhaseTransitionInvalid);
        }

        await Changed(id, cancellationToken);
    }

    public async Task AdvancePhase(
        long id,
        SetRetroPhaseRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureOwner(id, authData, cancellationToken);
        if (!await coreRetrosService.AdvancePhase(id, request.Phase, cancellationToken))
            throw new BadRequestException(nameof(request.Phase), ErrorMessages.RetroPhaseTransitionInvalid);

        await Changed(id, cancellationToken);
    }

    public async Task RevertPhase(
        long id,
        SetRetroPhaseRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureOwner(id, authData, cancellationToken);
        if (!await coreRetrosService.RevertPhase(id, request.Phase, cancellationToken))
            throw new BadRequestException(nameof(request.Phase), ErrorMessages.RetroPhaseTransitionInvalid);

        await Changed(id, cancellationToken);
    }

    public async Task SetPhaseTimer(
        long id,
        SetRetroTimerRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureOwner(id, authData, cancellationToken);
        await coreRetrosService.SetPhaseTimer(id, request.Minutes, cancellationToken);
        await Changed(id, cancellationToken);
    }

    public async Task SetDiscussedCard(
        long id,
        SetRetroDiscussedCardRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureOwner(id, authData, cancellationToken);
        if (!await coreRetrosService.SetDiscussedCard(id, request.CardId, cancellationToken))
            throw CardNotFound(request.CardId!.Value);

        await Changed(id, cancellationToken);
    }

    public async Task SetMyCardsRevealed(
        long id,
        SetRetroRevealRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var phase = await EnsureEditable(id, authData, cancellationToken);
        EnsureCardEditable(isAction: false, phase, nameof(id));
        await coreRetrosService.SetCardsRevealed(
            id,
            authData.UserId,
            request.Revealed,
            cancellationToken);
        await Changed(id, cancellationToken);
    }

    public async Task<CreateRetroCardResponse> CreateCard(
        long id,
        CreateRetroCardRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var phase = await EnsureEditable(id, authData, cancellationToken);
        var isAction = await EnsureSectionInRetro(id, request.SectionId, cancellationToken);
        EnsureCardEditable(isAction, phase, nameof(request.SectionId));
        var cardId = await coreRetrosService.CreateCard(
            request.SectionId,
            authData.UserId,
            request.Text.Trim(),
            request.X,
            request.Y,
            cancellationToken);
        await Changed(id, cancellationToken);
        return new CreateRetroCardResponse { Id = cardId };
    }

    public async Task UpdateCard(
        Guid cardId,
        UpdateRetroCardRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var card = await EnsureOwnCard(cardId, authData, cancellationToken);
        EnsureCardEditable(card.IsAction, card.Phase, nameof(cardId));
        await coreRetrosService.UpdateCard(cardId, request.Text.Trim(), cancellationToken);
        await Changed(card.RetroId, cancellationToken);
    }

    public async Task MoveCard(
        Guid cardId,
        MoveRetroCardRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var card = await EnsureCard(cardId, authData, cancellationToken);
        var targetIsAction = await EnsureSectionInRetro(
            card.RetroId,
            request.SectionId,
            cancellationToken);
        if (card.Finished)
            throw new BadRequestException(nameof(request.SectionId), ErrorMessages.RetroFinished);

        // Sliding a note around the board is layout, not content, so every phase allows it. Only
        // crossing the actions border turns the note into something else - and a note that crossed
        // it by mistake has to be able to cross back, so both ways belong to the Actions phase.
        if (targetIsAction != card.IsAction)
            EnsureCardEditable(isAction: true, card.Phase, nameof(request.SectionId));
        await coreRetrosService.MoveCard(
            cardId,
            request.SectionId,
            request.X,
            request.Y,
            cancellationToken);
        await Changed(card.RetroId, cancellationToken);
    }

    public async Task MoveGroup(
        long id,
        long groupId,
        MoveRetroGroupRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var phase = await EnsureEditable(id, authData, cancellationToken);
        // A topic is notes of one category, so it lands in a section as a whole - and the actions
        // section stays as closed to it as it is to a single note.
        if (await EnsureSectionInRetro(id, request.SectionId, cancellationToken))
            EnsureCardEditable(isAction: true, phase, nameof(request.SectionId));

        if (!await coreRetrosService.MoveGroup(
                id,
                groupId,
                request.SectionId,
                request.DeltaX,
                request.DeltaY,
                cancellationToken))
        {
            throw GroupNotFound(groupId);
        }

        await Changed(id, cancellationToken);
    }

    public async Task DeleteCard(
        Guid cardId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var card = await EnsureOwnCard(cardId, authData, cancellationToken);
        EnsureCardEditable(card.IsAction, card.Phase, nameof(cardId));
        await coreRetrosService.DeleteCard(cardId, cancellationToken);
        await Changed(card.RetroId, cancellationToken);
    }

    public async Task SetCardVote(
        Guid cardId,
        SetRetroCardVoteRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var card = await EnsureCard(cardId, authData, cancellationToken);
        var retroId = card.RetroId;
        var result = await coreRetrosService.SetVote(cardId, authData.UserId, request.Voted, cancellationToken);
        switch (result)
        {
            case RetroVoteResult.Added:
            case RetroVoteResult.Removed:
                await Changed(retroId, cancellationToken);
                return;
            case RetroVoteResult.TimerNotRunning:
                throw new BadRequestException(nameof(cardId), ErrorMessages.RetroVoteTimerNotRunning);
            case RetroVoteResult.LimitReached:
                throw new BadRequestException(nameof(cardId), ErrorMessages.RetroVoteLimitReached);
            default:
                throw CardNotFound(cardId);
        }
    }

    /// <summary>Wipes every vote of the retro so the room can vote again.</summary>
    public async Task ResetVotes(
        long id,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureOwner(id, authData, cancellationToken);
        await coreRetrosService.ResetVotes(id, cancellationToken);
        await Changed(id, cancellationToken);
    }

    public async Task SetCardDone(
        Guid cardId,
        SetRetroCardDoneRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var card = await EnsureCard(cardId, authData, cancellationToken);
        if (!await coreRetrosService.SetDone(cardId, request.Done, cancellationToken))
            throw new BadRequestException(nameof(cardId), ErrorMessages.RetroCardDoneUnavailable);

        await Changed(card.RetroId, cancellationToken);
    }

    public async Task SetCardRevealed(
        Guid cardId,
        SetRetroCardRevealedRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var card = await EnsureOwnCard(cardId, authData, cancellationToken);
        EnsureCardEditable(card.IsAction, card.Phase, nameof(cardId));
        await coreRetrosService.SetRevealed(cardId, request.Revealed, cancellationToken);
        await Changed(card.RetroId, cancellationToken);
    }

    public async Task SetCardAssignee(
        Guid cardId,
        SetRetroCardAssigneeRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var card = await EnsureCard(cardId, authData, cancellationToken);
        if (!card.IsAction)
            throw new BadRequestException(nameof(cardId), ErrorMessages.RetroActionsSectionOnly);

        EnsureCardEditable(card.IsAction, card.Phase, nameof(cardId));

        if (request.AssigneeId.HasValue)
        {
            var isParticipant = await context.RetroParticipants.AnyAsync(
                x => x.RetroId == card.RetroId && x.UserId == request.AssigneeId.Value,
                cancellationToken);
            if (!isParticipant)
            {
                throw new BadRequestException(
                    nameof(request.AssigneeId),
                    ErrorMessages.RetroAssigneeNotParticipant);
            }
        }

        await coreRetrosService.SetAssignee(cardId, request.AssigneeId, cancellationToken);
        await Changed(card.RetroId, cancellationToken);
    }

    public async Task<GroupRetroCardsResponse> GroupCards(
        long id,
        GroupRetroCardsRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureGroupingAllowed(id, authData, cancellationToken);

        var groupId = await coreRetrosService.GroupCards(id, request.CardIds, cancellationToken)
            ?? throw new BadRequestException(nameof(request.CardIds), ErrorMessages.RetroGroupInvalid);

        await Changed(id, cancellationToken);

        return new GroupRetroCardsResponse { Id = groupId };
    }

    public async Task Ungroup(
        long id,
        long groupId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureGroupingAllowed(id, authData, cancellationToken);

        if (!await coreRetrosService.Ungroup(id, groupId, cancellationToken))
            throw GroupNotFound(groupId);

        await Changed(id, cancellationToken);
    }

    public async Task SetGroupTitle(
        long id,
        long groupId,
        SetRetroGroupTitleRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureGroupingAllowed(id, authData, cancellationToken);

        if (!await coreRetrosService.SetGroupTitle(id, groupId, request.Title.Trim(), cancellationToken))
            throw GroupNotFound(groupId);

        await Changed(id, cancellationToken);
    }

    /// <summary>
    /// Topics are the facilitator's tool for running the room, not a step of it: a wrongly cut
    /// topic has to be fixable while the team is already discussing it.
    /// </summary>
    private async Task EnsureGroupingAllowed(
        long retroId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureOwner(retroId, authData, cancellationToken);
        await EnsureEditable(retroId, authData, cancellationToken);
    }

    private async Task EnsureMember(OrganizationAuthData authData, CancellationToken cancellationToken)
    {
        var isMember = await context.OrganizationUsers
            .Where(x => x.OrganizationId == authData.OrganizationId && x.UserId == authData.UserId)
            .AnyAsync(cancellationToken);
        if (!isMember)
            throw new NotFoundException(string.Format(
                ErrorMessages.EntityNotFoundOrNotAccessible,
                "Organization",
                authData.OrganizationId));
    }

    private Task<RetroUser> GetCurrentUser(
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        return context.OrganizationUsers
            .Where(x => x.OrganizationId == authData.OrganizationId && x.UserId == authData.UserId)
            .Select(x => new RetroUser
            {
                UserId = x.UserId,
                DisplayName = x.User!.DisplayName,
                Initials = x.User.Initials,
                Color = x.User.Color,
                IsCurrentUser = true,
            })
            .FirstOrThrowNotFoundEFAsync(
                string.Format(
                    ErrorMessages.EntityNotFoundOrNotAccessible,
                    "Organization",
                    authData.OrganizationId),
                cancellationToken);
    }

    private async Task EnsureAccessible(
        long retroId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureMember(authData, cancellationToken);
        if (!await OrganizationRetros(authData).AnyAsync(x => x.Id == retroId, cancellationToken))
            throw RetroNotFound(retroId);
    }

    /// <summary>A finished retro is a record of the past: everyone keeps read access only.</summary>
    private async Task<RetroPhase> EnsureEditable(
        long retroId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var retro = await OrganizationRetros(authData)
            .Where(x => x.Id == retroId)
            .Select(x => new { x.FinishedAt, x.Phase })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw RetroNotFound(retroId);

        if (retro.FinishedAt.HasValue)
            throw new BadRequestException(nameof(retroId), ErrorMessages.RetroFinished);

        return retro.Phase;
    }

    /// <summary>
    /// Topics are written in Collect/Group and freeze from Vote on, so everyone votes on and
    /// discusses the same set; the facilitator has to revert the retro to change them again.
    /// Action items are the mirror image - they only exist once the team is in Actions.
    /// </summary>
    private static void EnsureCardEditable(bool isAction, RetroPhase phase, string paramName)
    {
        if (isAction)
        {
            if (phase != RetroPhase.Actions)
                throw new BadRequestException(paramName, ErrorMessages.RetroActionsPhaseOnly);

            return;
        }

        if (phase is not (RetroPhase.Collect or RetroPhase.Group))
            throw new BadRequestException(paramName, ErrorMessages.RetroCardsFrozen);
    }

    /// <summary>Running the retro is the facilitator's job; returns when it was finished, if it was.</summary>
    private async Task<DateTime?> EnsureRetroOwner(
        long retroId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var retro = await OrganizationRetros(authData)
            .Where(x => x.Id == retroId)
            .Select(x => new { x.OwnerId, x.FinishedAt })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw RetroNotFound(retroId);

        if (retro.OwnerId != authData.UserId)
            throw new ForbiddenException(string.Format(
                ErrorMessages.EntityActionForbidden,
                "Retro",
                retroId,
                "manage"));

        return retro.FinishedAt;
    }

    private async Task EnsureOwner(
        long retroId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        if ((await EnsureRetroOwner(retroId, authData, cancellationToken)).HasValue)
            throw new BadRequestException(nameof(retroId), ErrorMessages.RetroFinished);
    }

    /// <summary>Checks the section belongs to the retro and tells whether it is the actions one.</summary>
    private async Task<bool> EnsureSectionInRetro(
        long retroId,
        long sectionId,
        CancellationToken cancellationToken)
    {
        var section = await context.RetroSections
            .Where(x => x.Id == sectionId && x.RetroId == retroId)
            .Select(x => new { IsAction = x.SortOrder == x.Retro!.Sections.Max(s => s.SortOrder) })
            .FirstOrDefaultAsync(cancellationToken);

        return section?.IsAction
            ?? throw new BadRequestException(nameof(sectionId), ErrorMessages.RetroSectionNotInRetro);
    }

    private async Task<CardContext> EnsureCard(
        Guid cardId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        return await CardContexts(authData)
            .Where(x => x.Id == cardId)
            .Select(CardContextSelector())
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw CardNotFound(cardId);
    }

    private async Task<CardContext> EnsureOwnCard(
        Guid cardId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var card = await EnsureCard(cardId, authData, cancellationToken);
        if (card.AuthorId != authData.UserId)
            throw new ForbiddenException(string.Format(
                ErrorMessages.EntityActionForbidden,
                "Card",
                cardId,
                "update"));

        return card;
    }

    private static Expression<Func<RetroCard, CardContext>> CardContextSelector() =>
        x => new CardContext(
            x.AuthorId,
            x.Section!.RetroId,
            x.Section.Retro!.Phase,
            x.Section.SortOrder == x.Section.Retro.Sections.Max(s => s.SortOrder),
            x.Section.Retro.FinishedAt != null);

    private record CardContext(
        Guid AuthorId,
        long RetroId,
        RetroPhase Phase,
        bool IsAction,
        bool Finished);

    private Task Changed(long retroId, CancellationToken cancellationToken) =>
        retroHub.Clients
            .Group(RetroHub.GroupName(retroId))
            .SendAsync("changed", cancellationToken);

    private IQueryable<RetroEntity> OrganizationRetros(OrganizationAuthData authData) =>
        context.Retros.Where(x => x.OrganizationId == authData.OrganizationId);

    /// <summary>Cards of the caller's organization that are still open for changes.</summary>
    private IQueryable<RetroCard> CardContexts(OrganizationAuthData authData) =>
        context.RetroCards.Where(x => x.Section!.Retro!.OrganizationId == authData.OrganizationId
            && x.Section.Retro.FinishedAt == null);

    private static NotFoundException RetroNotFound(long id) => new(string.Format(
        ErrorMessages.EntityNotFound,
        "Retro",
        id));

    private static NotFoundException CardNotFound(Guid id) => new(string.Format(
        ErrorMessages.EntityNotFound,
        "Card",
        id));

    private static NotFoundException GroupNotFound(long id) => new(string.Format(
        ErrorMessages.EntityNotFound,
        "Group",
        id));
}

public record RetroUser
{
    public required Guid UserId { get; set; }
    public required string DisplayName { get; set; }
    public required string Initials { get; set; }
    public required string Color { get; set; }
    public required bool IsCurrentUser { get; set; }
}

public record RetroListItem
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime? FinishedAt { get; init; }
    public required int CardCount { get; init; }

    /// <summary>Actions still open here, i.e. what starting a retro from this one would carry.</summary>
    public required int OpenActionCount { get; init; }
}

public record GetRetroResponse
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public required string Color { get; init; }
    public required RetroPhase Phase { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime? FinishedAt { get; init; }
    public required int VotesPerUser { get; init; }
    public required int MyVotes { get; init; }
    public required DateTime? PhaseEndsAt { get; init; }
    public required Guid? DiscussedCardId { get; init; }
    public required bool CanManage { get; init; }
    public required RetroUser Owner { get; init; }
    public required RetroUser CurrentUser { get; init; }
    public required RetroUser[] Participants { get; init; }
    public required RetroSectionDto[] Sections { get; init; }
    public required RetroCardDto[] Cards { get; init; }
    public required RetroCardGroupDto[] Groups { get; init; }
}

public record RetroCardGroupDto
{
    public required long Id { get; init; }
    public required string Title { get; init; }
    public required Guid[] CardIds { get; init; }
    public required int Votes { get; init; }
    public required bool VotedByMe { get; init; }
}

public record RetroSectionDto
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public required string Color { get; init; }
    public required int SortOrder { get; init; }
}

public record RetroCardDto
{
    public required Guid Id { get; init; }
    public required long SectionId { get; init; }
    public required string Text { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required int StackOrder { get; init; }
    public required bool Done { get; init; }
    public required bool Hidden { get; init; }
    public required bool Revealed { get; init; }
    public required bool IsMine { get; init; }
    public required RetroUser? Assignee { get; init; }
    public required long? GroupId { get; init; }
    public required int Votes { get; init; }
    public required bool VotedByMe { get; init; }
    public required RetroUser Author { get; init; }
}

public record CreateRetroRequest
{
    [MaxLength(128)]
    public required string Name { get; init; }

    /// <summary>Retro whose open actions are carried into the new one; null starts from scratch.</summary>
    public long? BasedOnRetroId { get; init; }
}

public record RenameRetroRequest
{
    [MaxLength(128)]
    public required string Name { get; init; }
}

public record CreateRetroResponse
{
    public required long Id { get; init; }
}

public record CreateRetroCardRequest
{
    public required long SectionId { get; init; }

    [MaxLength(4096)]
    public required string Text { get; init; }

    public required double X { get; init; }
    public required double Y { get; init; }
}

public record CreateRetroCardResponse
{
    public required Guid Id { get; init; }
}

public record UpdateRetroCardRequest
{
    [MaxLength(4096)]
    public required string Text { get; init; }
}

public record MoveRetroCardRequest
{
    public required long SectionId { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
}

public record MoveRetroGroupRequest
{
    public required long SectionId { get; init; }
    public required double DeltaX { get; init; }
    public required double DeltaY { get; init; }
}

public record UpdateRetroSettingsRequest
{
    public required RetroPhase Phase { get; init; }

    [Range(1, int.MaxValue)]
    public required int VotesPerUser { get; init; }
}

public record TransferRetroOwnershipRequest
{
    /// <summary>Participant who runs the retro from now on.</summary>
    public required Guid UserId { get; init; }
}

public record SetRetroPhaseRequest
{
    public required RetroPhase Phase { get; init; }
}

public record SetRetroTimerRequest
{
    /// <summary>Runs the timer of the current phase for that many minutes; null stops it.</summary>
    [Range(1, int.MaxValue)]
    public required int? Minutes { get; init; }
}

public record SetRetroDiscussedCardRequest
{
    /// <summary>The topic being discussed right now; null clears it. Resets the timer either way.</summary>
    public required Guid? CardId { get; init; }
}

public record SetRetroRevealRequest
{
    public required bool Revealed { get; init; }
}

public record SetRetroCardVoteRequest
{
    public required bool Voted { get; init; }
}

public record SetRetroCardDoneRequest
{
    public required bool Done { get; init; }
}

public record SetRetroCardRevealedRequest
{
    public required bool Revealed { get; init; }
}

public record GroupRetroCardsRequest
{
    /// <summary>The notes to merge; at least two, all topics of this retro.</summary>
    public required Guid[] CardIds { get; init; }
}

public record GroupRetroCardsResponse
{
    public required long Id { get; init; }
}

public record SetRetroGroupTitleRequest
{
    [MaxLength(4096)]
    public required string Title { get; init; }
}

public record SetRetroCardAssigneeRequest
{
    /// <summary>A retro participant taking the action on; null drops the assignment.</summary>
    public required Guid? AssigneeId { get; init; }
}
