using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DateTime.Services.Abstractions;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
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
        long? basedOnRetroId,
        CancellationToken cancellationToken);
    Task Join(long retroId, Guid userId, CancellationToken cancellationToken);
    Task<bool> SetOwner(long retroId, Guid userId, CancellationToken cancellationToken);
    Task Delete(long retroId, CancellationToken cancellationToken);
    Task Rename(long retroId, string name, CancellationToken cancellationToken);
    Task<bool> Finish(long retroId, CancellationToken cancellationToken);
    Task<bool> UpdateSettings(
        long retroId,
        RetroPhase phase,
        int votesPerUser,
        CancellationToken cancellationToken);
    Task<bool> AdvancePhase(long retroId, RetroPhase phase, CancellationToken cancellationToken);
    Task<bool> RevertPhase(long retroId, RetroPhase phase, CancellationToken cancellationToken);
    Task SetPhaseTimer(long retroId, int? minutes, CancellationToken cancellationToken);
    Task<bool> SetDiscussedCard(long retroId, Guid? cardId, CancellationToken cancellationToken);
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
    Task<bool> MoveGroup(
        long retroId,
        long groupId,
        long sectionId,
        double deltaX,
        double deltaY,
        CancellationToken cancellationToken);
    Task DeleteCard(Guid cardId, CancellationToken cancellationToken);
    Task<bool> SetDone(Guid cardId, bool done, CancellationToken cancellationToken);
    Task SetAssignee(Guid cardId, Guid? assigneeId, CancellationToken cancellationToken);
    Task<long?> GroupCards(
        long retroId,
        IReadOnlyCollection<Guid> cardIds,
        CancellationToken cancellationToken);
    Task<bool> Ungroup(long retroId, long groupId, CancellationToken cancellationToken);
    Task<bool> SetGroupTitle(
        long retroId,
        long groupId,
        string title,
        CancellationToken cancellationToken);
    Task SetRevealed(Guid cardId, bool revealed, CancellationToken cancellationToken);
    Task<RetroVoteResult> SetVote(Guid cardId, Guid userId, bool voted, CancellationToken cancellationToken);
}

public class CoreRetrosService(DatabaseContext context, IDateTimeProvider dateTimeProvider)
    : ICoreRetrosService
{
    private static readonly RetroPhase[] PhaseOrder =
    [
        RetroPhase.Collect,
        RetroPhase.Group,
        RetroPhase.Vote,
        RetroPhase.Discuss,
        RetroPhase.Actions,
    ];

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

    /// <summary>
    /// Starts a retro. Nothing is carried over on its own - when the facilitator picks a retro to
    /// build on, its open actions come along, owners included.
    /// </summary>
    public async Task<long> Create(
        long organizationId,
        Guid ownerId,
        string name,
        long? basedOnRetroId,
        CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;
        var carriedCards = Array.Empty<RetroCard>();
        if (basedOnRetroId.HasValue)
        {
            var actionSectionId = await context.RetroSections
                .Where(x => x.RetroId == basedOnRetroId.Value)
                .OrderByDescending(x => x.SortOrder)
                .Select(x => (long?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (actionSectionId.HasValue)
            {
                var previousCards = await context.RetroCards
                    .Where(x => x.SectionId == actionSectionId.Value && !x.Done)
                    .Select(x => new { x.AuthorId, x.AssigneeId, x.Text, x.X, x.Y, x.CreatedAt })
                    .ToArrayAsync(cancellationToken);

                carriedCards = previousCards
                    .Select(x => new RetroCard
                    {
                        Id = Guid.NewGuid(),
                        AuthorId = x.AuthorId,
                        AssigneeId = x.AssigneeId,
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
        };

        context.Retros.Add(retro);
        await context.SaveChangesAsync(cancellationToken);

        return retro.Id;
    }

    public Task Join(long retroId, Guid userId, CancellationToken cancellationToken)
    {
        var participant = context.Retros
            .Where(x => x.Id == retroId && x.FinishedAt == null)
            .Select(x => new RetroParticipant { RetroId = x.Id, UserId = userId });

        return context.RetroParticipants
            .Merge()
            .Using(participant.ToLinqToDB())
            .On((target, source) => target.RetroId == source.RetroId && target.UserId == source.UserId)
            .InsertWhenNotMatched()
            .MergeAsync(cancellationToken);
    }

    public Task Rename(long retroId, string name, CancellationToken cancellationToken)
    {
        return context.Retros
            .Where(x => x.Id == retroId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Name, name), cancellationToken);
    }

    /// <summary>Removes the retro with everything on its board - sections, notes, votes.</summary>
    public Task Delete(long retroId, CancellationToken cancellationToken)
    {
        return context.Retros
            .Where(x => x.Id == retroId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Hands the retro over to somebody else; the previous owner keeps no control.</summary>
    public async Task<bool> SetOwner(long retroId, Guid userId, CancellationToken cancellationToken)
    {
        var updated = await context.Retros
            .Where(x => x.Id == retroId && x.FinishedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.OwnerId, userId), cancellationToken);

        return updated != 0;
    }

    public async Task<bool> Finish(long retroId, CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;
        var updated = await context.Retros
            .Where(x => x.Id == retroId && x.Phase == RetroPhase.Actions && x.FinishedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.FinishedAt, now), cancellationToken);

        return updated != 0;
    }

    public async Task<bool> UpdateSettings(
        long retroId,
        RetroPhase phase,
        int votesPerUser,
        CancellationToken cancellationToken)
    {
        var updated = await context.Retros
            .Where(x => x.Id == retroId && x.Phase == phase && x.FinishedAt == null)
            .ExecuteUpdateAsync(
                x => x.SetProperty(p => p.VotesPerUser, votesPerUser),
                cancellationToken);

        return updated != 0;
    }

    public Task<bool> AdvancePhase(
        long retroId,
        RetroPhase phase,
        CancellationToken cancellationToken) =>
        MovePhase(retroId, phase, 1, cancellationToken);

    public Task<bool> RevertPhase(
        long retroId,
        RetroPhase phase,
        CancellationToken cancellationToken) =>
        MovePhase(retroId, phase, -1, cancellationToken);

    private async Task<bool> MovePhase(
        long retroId,
        RetroPhase phase,
        int offset,
        CancellationToken cancellationToken)
    {
        var current = await context.Retros
            .Where(x => x.Id == retroId && x.FinishedAt == null)
            .Select(x => x.Phase)
            .FirstOrDefaultAsync(cancellationToken);
        var currentIndex = Array.IndexOf(PhaseOrder, current);
        var targetIndex = currentIndex + offset;

        if (targetIndex < 0 || targetIndex >= PhaseOrder.Length || PhaseOrder[targetIndex] != phase)
            return false;

        var updated = await context.Retros
            .Where(x => x.Id == retroId && x.Phase == current && x.FinishedAt == null)
            .ExecuteUpdateAsync(
                x => x
                    .SetProperty(p => p.Phase, phase)
                    .SetProperty(p => p.PhaseEndsAt, (DateTime?)null)
                    .SetProperty(p => p.DiscussedCardId, (Guid?)null),
                cancellationToken);

        return updated != 0;
    }

    /// <summary>
    /// Runs the timer of the current phase for <paramref name="minutes"/> from now; null stops it.
    /// Extending is the same call with a bigger number - the deadline is always counted from now,
    /// so the facilitator never has to work out what is left.
    /// </summary>
    public Task SetPhaseTimer(long retroId, int? minutes, CancellationToken cancellationToken)
    {
        var endsAt = minutes.HasValue
            ? dateTimeProvider.UtcNow.AddMinutes(minutes.Value)
            : (DateTime?)null;

        return context.Retros
            .Where(x => x.Id == retroId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.PhaseEndsAt, endsAt), cancellationToken);
    }

    /// <summary>Moves the discussion to another topic, which stops the timer of the previous one.</summary>
    public async Task<bool> SetDiscussedCard(
        long retroId,
        Guid? cardId,
        CancellationToken cancellationToken)
    {
        if (cardId.HasValue)
        {
            var belongsToRetro = await context.RetroCards
                .AnyAsync(x => x.Id == cardId.Value && x.Section!.RetroId == retroId, cancellationToken);
            if (!belongsToRetro)
                return false;
        }

        await context.Retros
            .Where(x => x.Id == retroId)
            .ExecuteUpdateAsync(
                x => x
                    .SetProperty(p => p.DiscussedCardId, cardId)
                    .SetProperty(p => p.PhaseEndsAt, (DateTime?)null),
                cancellationToken);

        return true;
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
        var retroId = await RetroIdOfSection(sectionId, cancellationToken);
        var card = new RetroCard
        {
            Id = Guid.NewGuid(),
            SectionId = sectionId,
            AuthorId = authorId,
            Text = text,
            X = x,
            Y = y,
            StackOrder = await NextStackOrder(retroId, cancellationToken),
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

    /// <summary>Moves the card and lifts it above every other card of the same retro.</summary>
    public async Task MoveCard(
        Guid cardId,
        long sectionId,
        double x,
        double y,
        CancellationToken cancellationToken)
    {
        var stackOrder = await NextStackOrder(await RetroIdOfSection(sectionId, cancellationToken), cancellationToken);

        await context.RetroCards
            .Where(x => x.Id == cardId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(p => p.SectionId, sectionId)
                .SetProperty(p => p.X, x)
                .SetProperty(p => p.Y, y)
                .SetProperty(p => p.StackOrder, stackOrder),
                cancellationToken);
    }

    /// <summary>Moves every card of the group together and keeps their stacking order.</summary>
    public async Task<bool> MoveGroup(
        long retroId,
        long groupId,
        long sectionId,
        double deltaX,
        double deltaY,
        CancellationToken cancellationToken)
    {
        var cards = context.RetroCards
            .Where(x => x.GroupId == groupId && x.Section!.RetroId == retroId);
        var firstStackOrder = await cards.MinAsync(x => (int?)x.StackOrder, cancellationToken);

        if (!firstStackOrder.HasValue)
            return false;

        var nextStackOrder = await NextStackOrder(retroId, cancellationToken);
        var updated = await cards.ExecuteUpdateAsync(update => update
            .SetProperty(p => p.SectionId, sectionId)
            .SetProperty(p => p.X, p => p.X + deltaX)
            .SetProperty(p => p.Y, p => p.Y + deltaY)
            .SetProperty(p => p.StackOrder, p => nextStackOrder + p.StackOrder - firstStackOrder.Value),
            cancellationToken);

        return updated != 0;
    }

    private Task<long> RetroIdOfSection(long sectionId, CancellationToken cancellationToken) =>
        context.RetroSections
            .Where(s => s.Id == sectionId)
            .Select(s => s.RetroId)
            .FirstAsync(cancellationToken);

    // ponytail: read-then-write, so two drops landing in the same millisecond can share an order.
    // Harmless as long as the read side breaks ties deterministically (RetrosService orders by
    // StackOrder, then CreatedAt, then Id) - every client still paints the same stack. Swap for a
    // single "set stack_order = (select max...) + 1" statement if that ever stops being enough;
    // EF cannot translate that subquery inside ExecuteUpdate, so it would need raw SQL.
    private async Task<int> NextStackOrder(long retroId, CancellationToken cancellationToken)
    {
        var max = await context.RetroCards
            .Where(c => c.Section!.RetroId == retroId)
            .MaxAsync(c => (int?)c.StackOrder, cancellationToken);

        return (max ?? 0) + 1;
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
            .Where(x => x.Section!.Retro!.Phase == RetroPhase.Actions)
            .Where(x => x.Section!.SortOrder == x.Section.Retro!.Sections.Max(s => s.SortOrder))
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Done, done), cancellationToken);

        return updated != 0;
    }

    /// <summary>
    /// Merges the given notes into one topic. Returns null when the selection does not hold up -
    /// fewer than two notes, or a note that is not a topic of this retro.
    /// </summary>
    public async Task<long?> GroupCards(
        long retroId,
        IReadOnlyCollection<Guid> cardIds,
        CancellationToken cancellationToken)
    {
        if (cardIds.Count < 2)
            return null;

        var actionsSortOrder = await context.RetroSections
            .Where(x => x.RetroId == retroId)
            .MaxAsync(x => (int?)x.SortOrder, cancellationToken);
        var found = await context.RetroCards
            .Where(x => cardIds.Contains(x.Id)
                && x.Section!.RetroId == retroId
                && x.Section.SortOrder != actionsSortOrder)
            .CountAsync(cancellationToken);

        if (found != cardIds.Count)
            return null;

        var group = new RetroCardGroup { RetroId = retroId, Title = string.Empty };
        context.RetroCardGroups.Add(group);
        await context.SaveChangesAsync(cancellationToken);

        await context.RetroCards
            .Where(x => cardIds.Contains(x.Id))
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.GroupId, group.Id), cancellationToken);

        // A note pulled out of its old topic can leave it with a single member, which is no longer
        // a topic at all - drop those so the board never shows a group of one.
        await DropDegenerateGroups(retroId, cancellationToken);

        return group.Id;
    }

    public async Task<bool> Ungroup(long retroId, long groupId, CancellationToken cancellationToken)
    {
        var deleted = await context.RetroCardGroups
            .Where(x => x.Id == groupId && x.RetroId == retroId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted != 0;
    }

    public async Task<bool> SetGroupTitle(
        long retroId,
        long groupId,
        string title,
        CancellationToken cancellationToken)
    {
        var updated = await context.RetroCardGroups
            .Where(x => x.Id == groupId && x.RetroId == retroId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Title, title), cancellationToken);

        return updated != 0;
    }

    private Task DropDegenerateGroups(long retroId, CancellationToken cancellationToken) =>
        context.RetroCardGroups
            .Where(x => x.RetroId == retroId && x.Cards.Count < 2)
            .ExecuteDeleteAsync(cancellationToken);

    public Task SetAssignee(Guid cardId, Guid? assigneeId, CancellationToken cancellationToken)
    {
        return context.RetroCards
            .Where(x => x.Id == cardId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.AssigneeId, assigneeId), cancellationToken);
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
                x.Section.Retro.PhaseEndsAt,
                x.Section.Retro.VotesPerUser,
                x.GroupId,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (card is null)
            return RetroVoteResult.CardNotFound;

        // A grouped note is not a topic of its own: the whole group shares one vote per person, so
        // every vote for any of its notes lands on its first note. An older vote can still sit on
        // any other note of the group - grouping does not move votes - so taking the vote back
        // looks at the whole group, not only at the note the new ones land on.
        var ballot = card.GroupId is null
            ? [cardId]
            : await context.RetroCards
                .Where(x => x.GroupId == card.GroupId)
                .OrderBy(x => x.StackOrder)
                .ThenBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

        cardId = ballot[0];

        if (card.Phase != RetroPhase.Vote
            || !card.PhaseEndsAt.HasValue
            || card.PhaseEndsAt <= dateTimeProvider.UtcNow)
        {
            return RetroVoteResult.TimerNotRunning;
        }

        var existingVotes = await context.RetroCardVotes
            .Where(x => ballot.Contains(x.CardId) && x.UserId == userId)
            .ToListAsync(cancellationToken);

        if (!voted)
        {
            if (existingVotes.Count != 0)
            {
                context.RetroCardVotes.RemoveRange(existingVotes);
                await context.SaveChangesAsync(cancellationToken);
            }

            return RetroVoteResult.Removed;
        }

        if (existingVotes.Count != 0)
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
