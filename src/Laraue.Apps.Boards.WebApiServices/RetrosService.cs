using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.Exceptions.Web;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.WebApiServices;

public interface IRetrosService
{
    Task<RetroListItem[]> Get(OrganizationAuthData authData, CancellationToken cancellationToken);
    Task<VisibleUser> JoinRealtime(
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
    Task ToggleVote(Guid cardId, OrganizationAuthData authData, CancellationToken cancellationToken);
    Task ToggleDone(Guid cardId, OrganizationAuthData authData, CancellationToken cancellationToken);
    Task ToggleReveal(Guid cardId, OrganizationAuthData authData, CancellationToken cancellationToken);
}

public class RetrosService(
    DatabaseContext context,
    IAccessService accessService,
    ICoreRetrosService coreRetrosService,
    IHubContext<RetroHub> retroHub) : IRetrosService
{
    public async Task<VisibleUser> JoinRealtime(
        long id,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureParticipant(id, authData, cancellationToken);
        return await GetCurrentUser(authData, cancellationToken);
    }

    public async Task<RetroListItem[]> Get(
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureMember(authData, cancellationToken);

        return await context.Retros
            .Where(x => x.OrganizationId == authData.OrganizationId)
            .Where(x => x.Participants.Any(p => p.UserId == authData.UserId))
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
        await EnsureParticipant(id, authData, cancellationToken);
        var currentUser = await GetCurrentUser(authData, cancellationToken);
        var retro = await context.Retros
            .AsNoTracking()
            .AsSplitQuery()
            .Where(x => x.Id == id && x.OrganizationId == authData.OrganizationId)
            .Include(x => x.Sections)
                .ThenInclude(x => x.Cards)
                    .ThenInclude(x => x.Author)
            .Include(x => x.Sections)
                .ThenInclude(x => x.Cards)
                    .ThenInclude(x => x.Votes)
            .Include(x => x.Owner)
            .Include(x => x.Participants)
                .ThenInclude(x => x.User)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw RetroNotFound(id);

        var cards = retro.Sections
            .SelectMany(x => x.Cards)
            .OrderBy(x => x.CreatedAt)
            .Select(card => new RetroCardDto
            {
                Id = card.Id,
                SectionId = card.SectionId,
                // A covered note must not reach the client at all - the UI only blurs it.
                Text = IsHidden(retro, card, authData.UserId) ? string.Empty : card.Text,
                X = card.X,
                Y = card.Y,
                Done = card.Done,
                Hidden = IsHidden(retro, card, authData.UserId),
                Revealed = card.Revealed,
                IsMine = card.AuthorId == authData.UserId,
                Votes = retro.Phase == RetroPhase.Collect ? 0 : card.Votes.Count,
                VotedByMe = card.Votes.Any(x => x.UserId == authData.UserId),
                Author = new VisibleUser
                {
                    UserId = card.AuthorId,
                    DisplayName = card.Author!.DisplayName,
                    Initials = card.Author.Initials,
                    Color = card.Author.Color,
                    IsCurrentUser = card.AuthorId == authData.UserId,
                },
            })
            .ToArray();

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
            Owner = MapUser(retro.Owner!, authData.UserId),
            CurrentUser = currentUser,
            Participants = retro.Participants
                .Select(x => MapUser(x.User!, authData.UserId))
                .OrderByDescending(x => x.IsCurrentUser)
                .ThenBy(x => x.DisplayName)
                .ToArray(),
            Sections = retro.Sections
                .OrderBy(x => x.SortOrder)
                .Select(x => new RetroSectionDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Color = x.Color,
                    SortOrder = x.SortOrder,
                })
                .ToArray(),
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
        await coreRetrosService.Finish(id, cancellationToken);
        await Changed(id, cancellationToken);
    }

    public async Task UpdateSettings(
        long id,
        UpdateRetroSettingsRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        await EnsureOwner(id, authData, cancellationToken);
        await coreRetrosService.UpdateSettings(
            id,
            request.Phase,
            request.VotesPerUser,
            cancellationToken);
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
        var cardId = await coreRetrosService.CreateCard(
            id,
            request.SectionId,
            authData.UserId,
            request.Text,
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
        await coreRetrosService.UpdateCard(cardId, request.Text, cancellationToken);
        await Changed(retroId, cancellationToken);
    }

    public async Task MoveCard(
        Guid cardId,
        MoveRetroCardRequest request,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var retroId = await EnsureCard(cardId, authData, cancellationToken);
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

    public async Task ToggleVote(
        Guid cardId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var retroId = await EnsureCard(cardId, authData, cancellationToken);
        var result = await coreRetrosService.ToggleVote(cardId, authData.UserId, cancellationToken);
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

    public async Task ToggleDone(
        Guid cardId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var retroId = await EnsureCard(cardId, authData, cancellationToken);
        if (!await coreRetrosService.ToggleDone(cardId, cancellationToken))
            throw new BadRequestException(nameof(cardId), ErrorMessages.RetroCardDoneUnavailable);

        await Changed(retroId, cancellationToken);
    }

    public async Task ToggleReveal(
        Guid cardId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var retroId = await EnsureOwnCard(cardId, authData, cancellationToken);
        await coreRetrosService.ToggleReveal(cardId, cancellationToken);
        await Changed(retroId, cancellationToken);
    }

    private async Task EnsureMember(OrganizationAuthData authData, CancellationToken cancellationToken)
    {
        var isMember = await accessService.GetOrganizations(
            authData.UserId,
            query => query
                .Where(x => x.OrganizationId == authData.OrganizationId)
                .AnyAsync(cancellationToken));
        if (!isMember)
            throw new NotFoundException(string.Format(
                ErrorMessages.EntityNotFoundOrNotAccessible,
                "Organization",
                authData.OrganizationId));
    }

    private Task<VisibleUser> GetCurrentUser(
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        return accessService.GetOrganizationMembers(
            authData.OrganizationId,
            query => query
                .Where(x => x.UserId == authData.UserId)
                .Select(x => new VisibleUser
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
                    cancellationToken));
    }

    private async Task EnsureParticipant(
        long retroId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        if (!await ParticipatingRetros(authData).AnyAsync(x => x.Id == retroId, cancellationToken))
            throw RetroNotFound(retroId);
    }

    /// <summary>A finished retro is a record of the past: everyone keeps read access only.</summary>
    private async Task EnsureEditable(
        long retroId,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var retro = await ParticipatingRetros(authData)
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
        var retro = await ParticipatingRetros(authData)
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

    private IQueryable<Retro> ParticipatingRetros(OrganizationAuthData authData) =>
        context.Retros.Where(x => x.OrganizationId == authData.OrganizationId
            && x.Participants.Any(p => p.UserId == authData.UserId));

    /// <summary>Cards of retros the caller takes part in that are still open for changes.</summary>
    private IQueryable<RetroCard> EditableCards(OrganizationAuthData authData) =>
        context.RetroCards.Where(x => x.Section!.Retro!.OrganizationId == authData.OrganizationId
            && x.Section.Retro.FinishedAt == null
            && x.Section.Retro.Participants.Any(p => p.UserId == authData.UserId));

    private static bool IsHidden(Retro retro, RetroCard card, Guid currentUserId) =>
        retro.Phase == RetroPhase.Collect && card.AuthorId != currentUserId && !card.Revealed;

    private static NotFoundException RetroNotFound(long id) => new(string.Format(
        ErrorMessages.EntityNotFound,
        "Retro",
        id));

    private static NotFoundException CardNotFound(Guid id) => new(string.Format(
        ErrorMessages.EntityNotFound,
        "Card",
        id));

    private static VisibleUser MapUser(User user, Guid currentUserId) => new()
    {
        UserId = user.Id,
        DisplayName = user.DisplayName,
        Initials = user.Initials,
        Color = user.Color,
        IsCurrentUser = user.Id == currentUserId,
    };
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
    public required VisibleUser Owner { get; init; }
    public required VisibleUser CurrentUser { get; init; }
    public required VisibleUser[] Participants { get; init; }
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
    public required VisibleUser Author { get; init; }
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
