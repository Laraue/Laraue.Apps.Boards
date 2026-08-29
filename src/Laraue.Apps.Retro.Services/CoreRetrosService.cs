using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
// "Retro" (the entity) collides with the "Laraue.Apps.Retro" namespace segment this project
// lives under - alias it so the bare type name resolves correctly.
using RetroEntity = Laraue.Apps.Boards.DataAccess.Models.Retro;

namespace Laraue.Apps.Retro.Services;

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
    Task<bool> SetDone(Guid cardId, bool done, CancellationToken cancellationToken);
    Task SetRevealed(Guid cardId, bool revealed, CancellationToken cancellationToken);
    Task<RetroVoteResult> SetVote(Guid cardId, Guid userId, bool voted, CancellationToken cancellationToken);
}

public class CoreRetrosService(DatabaseContext context, IDateTimeProvider dateTimeProvider)
    : ICoreRetrosService
{
    private static class SectionTemplate
    {
        private static readonly (string Name, string Color)[] Sections =
        [
            ("Good", "#489c61"),
            ("Bad", "#d65f63"),
            ("Start", "#4774d4"),
            ("Stop", "#c99724"),
            ("Actions", "#8a5fc1"),
        ];

        public static List<RetroSection> WithPreviousCards(IReadOnlyCollection<RetroCard> carriedCards)
        {
            var sections = Sections
                .Select((section, sortOrder) => new RetroSection
                {
                    Name = section.Name,
                    Color = section.Color,
                    SortOrder = sortOrder,
                })
                .ToList();
            sections[^1].Cards.AddRange(carriedCards);

            return sections;
        }
    }

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

        var retro = new RetroEntity
        {
            OrganizationId = organizationId,
            OwnerId = ownerId,
            Name = string.IsNullOrWhiteSpace(name) ? now.ToString("yyyy-MM-dd") : name.Trim(),
            Color = "#4774d4",
            Phase = RetroPhase.Collect,
            VotesPerUser = 3,
            CreatedAt = now,
            Sections = SectionTemplate.WithPreviousCards(carriedCards),
            Participants = participantIds
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
        long sectionId,
        Guid authorId,
        string text,
        double x,
        double y,
        CancellationToken cancellationToken)
    {
        var card = new RetroCard
        {
            Id = Guid.NewGuid(),
            SectionId = sectionId,
            AuthorId = authorId,
            Text = text,
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
        return context.RetroCards
            .Where(x => x.Id == cardId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Text, text), cancellationToken);
    }

    public Task MoveCard(
        Guid cardId,
        long sectionId,
        double x,
        double y,
        CancellationToken cancellationToken)
    {
        return context.RetroCards
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

    public async Task<bool> SetDone(Guid cardId, bool done, CancellationToken cancellationToken)
    {
        var updated = await context.RetroCards
            .Where(x => x.Id == cardId)
            .Where(x => x.Section!.Retro!.Phase == RetroPhase.Discuss)
            .Where(x => x.Section!.SortOrder == x.Section.Retro!.Sections.Max(s => s.SortOrder))
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Done, done), cancellationToken);

        return updated != 0;
    }

    public Task SetRevealed(Guid cardId, bool revealed, CancellationToken cancellationToken)
    {
        return context.RetroCards
            .Where(x => x.Id == cardId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Revealed, revealed), cancellationToken);
    }

    public async Task<RetroVoteResult> SetVote(
        Guid cardId,
        Guid userId,
        bool voted,
        CancellationToken cancellationToken)
    {
        var card = await context.RetroCards
            .Where(x => x.Id == cardId)
            .Select(x => new
            {
                RetroId = x.Section!.RetroId,
                x.Section.Retro!.Phase,
                x.Section.Retro.VoteEndsAt,
                x.Section.Retro.VotesPerUser,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (card is null)
            return RetroVoteResult.CardNotFound;

        if (card.Phase != RetroPhase.Vote
            || !card.VoteEndsAt.HasValue
            || card.VoteEndsAt <= dateTimeProvider.UtcNow)
        {
            return RetroVoteResult.TimerNotRunning;
        }

        var existingVote = await context.RetroCardVotes
            .FirstOrDefaultAsync(x => x.CardId == cardId && x.UserId == userId, cancellationToken);

        if (!voted)
        {
            if (existingVote is not null)
            {
                context.RetroCardVotes.Remove(existingVote);
                await context.SaveChangesAsync(cancellationToken);
            }

            return RetroVoteResult.Removed;
        }

        if (existingVote is not null)
            return RetroVoteResult.Added;

        var usedVotes = await context.RetroCardVotes
            .CountAsync(x => x.UserId == userId && x.Card!.Section!.RetroId == card.RetroId, cancellationToken);
        if (usedVotes >= card.VotesPerUser)
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
