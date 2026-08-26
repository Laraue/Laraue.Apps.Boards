using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

public interface ICoreRetrosService
{
    Task<long> Create(
        long organizationId,
        Guid ownerId,
        string name,
        CancellationToken cancellationToken);
    Task Finish(long retroId, CancellationToken cancellationToken);
    Task UpdateSettings(
        long retroId,
        RetroPhase phase,
        int votesPerUser,
        CancellationToken cancellationToken);
    Task SetVoteTimer(long retroId, int? minutes, CancellationToken cancellationToken);
    Task SetCardsRevealed(long retroId, Guid authorId, bool revealed, CancellationToken cancellationToken);
    Task<Guid> CreateCard(
        long retroId,
        long sectionId,
        Guid authorId,
        string text,
        double x,
        double y,
        CancellationToken cancellationToken);
    Task UpdateCard(Guid cardId, string text, CancellationToken cancellationToken);
    Task MoveCard(
        Guid cardId,
        long sectionId,
        double x,
        double y,
        CancellationToken cancellationToken);
    Task DeleteCard(Guid cardId, CancellationToken cancellationToken);
    Task<bool> ToggleDone(Guid cardId, CancellationToken cancellationToken);
    Task ToggleReveal(Guid cardId, CancellationToken cancellationToken);
    Task<RetroVoteResult> ToggleVote(Guid cardId, Guid userId, CancellationToken cancellationToken);
}

public class CoreRetrosService(DatabaseContext context, IDateTimeProvider dateTimeProvider)
    : ICoreRetrosService
{
    private static readonly (string Name, string Color)[] SectionTemplate =
    [
        ("Good", "#489c61"),
        ("Bad", "#d65f63"),
        ("Start", "#4774d4"),
        ("Stop", "#c99724"),
        ("Actions", "#8a5fc1"),
    ];

    public async Task<long> Create(
        long organizationId,
        Guid ownerId,
        string name,
        CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;
        var participantIds = await context.OrganizationUsers
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => x.UserId)
            .ToArrayAsync(cancellationToken);
        var previousRetroId = await context.Retros
            .Where(x => x.OrganizationId == organizationId && x.FinishedAt != null)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var carriedCards = Array.Empty<RetroCard>();
        if (previousRetroId.HasValue)
        {
            var actionSectionId = await context.RetroSections
                .Where(x => x.RetroId == previousRetroId.Value)
                .OrderByDescending(x => x.SortOrder)
                .Select(x => (long?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (actionSectionId.HasValue)
            {
                var previousCards = await context.RetroCards
                    .Where(x => x.SectionId == actionSectionId.Value && !x.Done)
                    .Select(x => new { x.AuthorId, x.Text, x.X, x.Y, x.CreatedAt })
                    .ToArrayAsync(cancellationToken);

                carriedCards = previousCards
                    .Select(x => new RetroCard
                    {
                        Id = Guid.NewGuid(),
                        AuthorId = x.AuthorId,
                        Text = x.Text,
                        X = x.X,
                        Y = x.Y,
                        Revealed = true,
                        CreatedAt = x.CreatedAt,
                    })
                    .ToArray();
            }
        }

        var sections = SectionTemplate
            .Select((section, sortOrder) => new RetroSection
            {
                Name = section.Name,
                Color = section.Color,
                SortOrder = sortOrder,
            })
            .ToList();
        sections[^1].Cards.AddRange(carriedCards);

        var retro = new Retro
        {
            OrganizationId = organizationId,
            OwnerId = ownerId,
            Name = string.IsNullOrWhiteSpace(name) ? now.ToString("yyyy-MM-dd") : name.Trim(),
            Color = "#4774d4",
            Phase = RetroPhase.Collect,
            VotesPerUser = 3,
            CreatedAt = now,
            Sections = sections,
            Participants = participantIds
                .Append(ownerId)
                .Distinct()
                .Select(userId => new RetroParticipant { UserId = userId })
                .ToList(),
        };

        context.Retros.Add(retro);
        await context.SaveChangesAsync(cancellationToken);

        return retro.Id;
    }

    public Task Finish(long retroId, CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;
        return context.Retros
            .Where(x => x.Id == retroId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.FinishedAt, now), cancellationToken);
    }

    public Task UpdateSettings(
        long retroId,
        RetroPhase phase,
        int votesPerUser,
        CancellationToken cancellationToken)
    {
        return context.Retros
            .Where(x => x.Id == retroId)
            .ExecuteUpdateAsync(
                x => x
                    .SetProperty(p => p.Phase, phase)
                    .SetProperty(p => p.VotesPerUser, votesPerUser),
                cancellationToken);
    }

    /// <summary>Starts the voting timer for <paramref name="minutes"/>; null stops it.</summary>
    public Task SetVoteTimer(long retroId, int? minutes, CancellationToken cancellationToken)
    {
        var endsAt = minutes.HasValue
            ? dateTimeProvider.UtcNow.AddMinutes(minutes.Value)
            : (DateTime?)null;

        return context.Retros
            .Where(x => x.Id == retroId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.VoteEndsAt, endsAt), cancellationToken);
    }

    public Task SetCardsRevealed(
        long retroId,
        Guid authorId,
        bool revealed,
        CancellationToken cancellationToken)
    {
        return context.RetroCards
            .Where(x => x.Section!.RetroId == retroId && x.AuthorId == authorId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Revealed, revealed), cancellationToken);
    }

    public async Task<Guid> CreateCard(
        long retroId,
        long sectionId,
        Guid authorId,
        string text,
        double x,
        double y,
        CancellationToken cancellationToken)
    {
        var sectionExists = await context.RetroSections
            .AnyAsync(x => x.Id == sectionId && x.RetroId == retroId, cancellationToken);
        if (!sectionExists)
            throw new BadRequestException(nameof(sectionId), "Section does not belong to the retro");

        var card = new RetroCard
        {
            Id = Guid.NewGuid(),
            SectionId = sectionId,
            AuthorId = authorId,
            Text = text.Trim(),
            X = x,
            Y = y,
            CreatedAt = dateTimeProvider.UtcNow,
        };

        context.RetroCards.Add(card);
        await context.SaveChangesAsync(cancellationToken);

        return card.Id;
    }

    public Task UpdateCard(Guid cardId, string text, CancellationToken cancellationToken)
    {
        var trimmedText = text.Trim();
        return context.RetroCards
            .Where(x => x.Id == cardId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Text, trimmedText), cancellationToken);
    }

    public async Task MoveCard(
        Guid cardId,
        long sectionId,
        double x,
        double y,
        CancellationToken cancellationToken)
    {
        var sourceRetroId = await context.RetroCards
            .Where(x => x.Id == cardId)
            .Select(x => (long?)x.Section!.RetroId)
            .FirstOrDefaultAsync(cancellationToken);
        var targetRetroId = await context.RetroSections
            .Where(x => x.Id == sectionId)
            .Select(x => (long?)x.RetroId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!sourceRetroId.HasValue || sourceRetroId != targetRetroId)
            throw new BadRequestException(nameof(sectionId), "Card can only be moved within its retro");

        await context.RetroCards
            .Where(x => x.Id == cardId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(p => p.SectionId, sectionId)
                .SetProperty(p => p.X, x)
                .SetProperty(p => p.Y, y),
                cancellationToken);
    }

    public Task DeleteCard(Guid cardId, CancellationToken cancellationToken)
    {
        return context.RetroCards
            .Where(x => x.Id == cardId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<bool> ToggleDone(Guid cardId, CancellationToken cancellationToken)
    {
        var updated = await context.RetroCards
            .Where(x => x.Id == cardId)
            .Where(x => x.Section!.Retro!.Phase == RetroPhase.Discuss)
            .Where(x => x.Section!.SortOrder == x.Section.Retro!.Sections.Max(s => s.SortOrder))
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Done, p => !p.Done), cancellationToken);

        return updated != 0;
    }

    public Task ToggleReveal(Guid cardId, CancellationToken cancellationToken)
    {
        return context.RetroCards
            .Where(x => x.Id == cardId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Revealed, p => !p.Revealed), cancellationToken);
    }

    public async Task<RetroVoteResult> ToggleVote(
        Guid cardId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var card = await context.RetroCards
            .Include(x => x.Section)!
            .ThenInclude(x => x.Retro)
            .Include(x => x.Votes)
            .FirstOrDefaultAsync(x => x.Id == cardId, cancellationToken);
        if (card?.Section?.Retro is not { } retro)
            return RetroVoteResult.CardNotFound;

        if (retro.Phase != RetroPhase.Vote
            || !retro.VoteEndsAt.HasValue
            || retro.VoteEndsAt <= dateTimeProvider.UtcNow)
        {
            return RetroVoteResult.TimerNotRunning;
        }

        var existingVote = card.Votes.FirstOrDefault(x => x.UserId == userId);
        if (existingVote is not null)
        {
            context.RetroCardVotes.Remove(existingVote);
            await context.SaveChangesAsync(cancellationToken);
            return RetroVoteResult.Removed;
        }

        var usedVotes = await context.RetroCardVotes
            .CountAsync(x => x.UserId == userId && x.Card!.Section!.RetroId == retro.Id, cancellationToken);
        if (usedVotes >= retro.VotesPerUser)
            return RetroVoteResult.LimitReached;

        context.RetroCardVotes.Add(new RetroCardVote { CardId = cardId, UserId = userId });
        await context.SaveChangesAsync(cancellationToken);

        return RetroVoteResult.Added;
    }
}

public enum RetroVoteResult
{
    Added,
    Removed,
    CardNotFound,
    TimerNotRunning,
    LimitReached,
}
