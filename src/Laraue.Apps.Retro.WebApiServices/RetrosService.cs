using System.ComponentModel.DataAnnotations;
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
    Task<RetroUser> JoinRealtime(
        long id,
        OrganizationAuthData authData,
        CancellationToken cancellationToken);
    Task<GetRetroResponse> Get(long id, OrganizationAuthData authData, CancellationToken cancellationToken);
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
    Task SetVoteTimer(
        long id,
        SetRetroTimerRequest request,
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
                x.VoteEndsAt,
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

        var cards = await context.RetroCards
            .Where(x => x.Section!.RetroId == id)
            .OrderBy(x => x.CreatedAt)
            .Select(c => new RetroCardDto
            {
                Id = c.Id,
                SectionId = c.SectionId,
                // A covered note must not reach the client at all - the UI only blurs it.
                Text = retro.Phase == RetroPhase.Collect && c.AuthorId != authData.UserId && !c.Revealed
                    ? string.Empty
                    : c.Text,
                X = c.X,
                Y = c.Y,
                Done = c.Done,
                Hidden = retro.Phase == RetroPhase.Collect && c.AuthorId != authData.UserId && !c.Revealed,
                Revealed = c.Revealed,
                IsMine = c.AuthorId == authData.UserId,
                Votes = retro.Phase == RetroPhase.Collect ? 0 : c.Votes.Count,
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
            VoteEndsAt = retro.VoteEndsAt,
            CanManage = retro.OwnerId == authData.UserId,
            Owner = retro.Owner,
            CurrentUser = currentUser,
            Participants = participants
                .OrderByDescending(x => x.IsCurrentUser)
                .ThenBy(x => x.DisplayName)
                .ToArray(),
            Sections = sections,
            Cards = cards,
        };
    }

    public async Task<CreateRetroResponse> Create(
        CreateRetroRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureMember(authData, cancellationToken);
        var id = await coreRetrosService.Create(
            authData.OrganizationId,
            authData.UserId,
            request.Name,
            cancellationToken);
        return new CreateRetroResponse { Id = id };
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

    public async Task SetVoteTimer(
        long id,
        SetRetroTimerRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureOwner(id, authData, cancellationToken);
        await coreRetrosService.SetVoteTimer(id, request.Minutes, cancellationToken);
        await Changed(id, cancellationToken);
    }

    public async Task SetMyCardsRevealed(
        long id,
        SetRetroRevealRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureEditable(id, authData, cancellationToken);
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
        await EnsureEditable(id, authData, cancellationToken);
        await EnsureSectionInRetro(id, request.SectionId, cancellationToken);
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
        var retroId = await EnsureOwnCard(cardId, authData, cancellationToken);
        await coreRetrosService.UpdateCard(cardId, request.Text.Trim(), cancellationToken);
        await Changed(retroId, cancellationToken);
    }

    public async Task MoveCard(
        Guid cardId,
        MoveRetroCardRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var retroId = await EnsureCard(cardId, authData, cancellationToken);
        await EnsureSectionInRetro(retroId, request.SectionId, cancellationToken);
        await coreRetrosService.MoveCard(
            cardId,
            request.SectionId,
            request.X,
            request.Y,
            cancellationToken);
        await Changed(retroId, cancellationToken);
    }

    public async Task DeleteCard(
        Guid cardId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var retroId = await EnsureOwnCard(cardId, authData, cancellationToken);
        await coreRetrosService.DeleteCard(cardId, cancellationToken);
        await Changed(retroId, cancellationToken);
    }

    public async Task SetCardVote(
        Guid cardId,
        SetRetroCardVoteRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var retroId = await EnsureCard(cardId, authData, cancellationToken);
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

    public async Task SetCardDone(
        Guid cardId,
        SetRetroCardDoneRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var retroId = await EnsureCard(cardId, authData, cancellationToken);
        if (!await coreRetrosService.SetDone(cardId, request.Done, cancellationToken))
            throw new BadRequestException(nameof(cardId), ErrorMessages.RetroCardDoneUnavailable);

        await Changed(retroId, cancellationToken);
    }

    public async Task SetCardRevealed(
        Guid cardId,
        SetRetroCardRevealedRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var retroId = await EnsureOwnCard(cardId, authData, cancellationToken);
        await coreRetrosService.SetRevealed(cardId, request.Revealed, cancellationToken);
        await Changed(retroId, cancellationToken);
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
    private async Task EnsureEditable(
        long retroId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var retro = await OrganizationRetros(authData)
            .Where(x => x.Id == retroId)
            .Select(x => new { x.FinishedAt })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw RetroNotFound(retroId);

        if (retro.FinishedAt.HasValue)
            throw new BadRequestException(nameof(retroId), ErrorMessages.RetroFinished);
    }

    private async Task EnsureOwner(
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

        if (retro.FinishedAt.HasValue)
            throw new BadRequestException(nameof(retroId), ErrorMessages.RetroFinished);
    }

    private async Task EnsureSectionInRetro(
        long retroId,
        long sectionId,
        CancellationToken cancellationToken)
    {
        var sectionExists = await context.RetroSections
            .AnyAsync(x => x.Id == sectionId && x.RetroId == retroId, cancellationToken);
        if (!sectionExists)
            throw new BadRequestException(nameof(sectionId), ErrorMessages.RetroSectionNotInRetro);
    }

    private async Task<long> EnsureCard(
        Guid cardId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var retroId = await EditableCards(authData)
            .Where(x => x.Id == cardId)
            .Select(x => (long?)x.Section!.RetroId)
            .FirstOrDefaultAsync(cancellationToken);

        return retroId ?? throw CardNotFound(cardId);
    }

    private async Task<long> EnsureOwnCard(
        Guid cardId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var card = await EditableCards(authData)
            .Where(x => x.Id == cardId)
            .Select(x => new { x.AuthorId, x.Section!.RetroId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw CardNotFound(cardId);

        if (card.AuthorId != authData.UserId)
            throw new ForbiddenException(string.Format(
                ErrorMessages.EntityActionForbidden,
                "Card",
                cardId,
                "update"));

        return card.RetroId;
    }

    private Task Changed(long retroId, CancellationToken cancellationToken) =>
        retroHub.Clients
            .Group(RetroHub.GroupName(retroId))
            .SendAsync("changed", cancellationToken);

    private IQueryable<RetroEntity> OrganizationRetros(OrganizationAuthData authData) =>
        context.Retros.Where(x => x.OrganizationId == authData.OrganizationId);

    /// <summary>Cards of the caller's organization that are still open for changes.</summary>
    private IQueryable<RetroCard> EditableCards(OrganizationAuthData authData) =>
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
    public required DateTime? VoteEndsAt { get; init; }
    public required bool CanManage { get; init; }
    public required RetroUser Owner { get; init; }
    public required RetroUser CurrentUser { get; init; }
    public required RetroUser[] Participants { get; init; }
    public required RetroSectionDto[] Sections { get; init; }
    public required RetroCardDto[] Cards { get; init; }
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
    public required bool Done { get; init; }
    public required bool Hidden { get; init; }
    public required bool Revealed { get; init; }
    public required bool IsMine { get; init; }
    public required int Votes { get; init; }
    public required bool VotedByMe { get; init; }
    public required RetroUser Author { get; init; }
}

public record CreateRetroRequest
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

public record UpdateRetroSettingsRequest
{
    public required RetroPhase Phase { get; init; }

    [Range(1, int.MaxValue)]
    public required int VotesPerUser { get; init; }
}

public record SetRetroPhaseRequest
{
    public required RetroPhase Phase { get; init; }
}

public record SetRetroTimerRequest
{
    /// <summary>Runs the voting timer for that many minutes; null stops it.</summary>
    [Range(1, int.MaxValue)]
    public required int? Minutes { get; init; }
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
