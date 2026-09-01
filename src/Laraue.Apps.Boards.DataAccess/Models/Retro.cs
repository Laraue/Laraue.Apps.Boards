using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class Retro
{
    public long Id { get; set; }

    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }

    [MaxLength(128)]
    public required string Name { get; set; }

    [MaxLength(7)]
    public required string Color { get; set; }

    public RetroPhase Phase { get; set; }
    public int VotesPerUser { get; set; }
    /// <summary>When the running timer of the current phase (or discussed topic) expires.</summary>
    public DateTime? PhaseEndsAt { get; set; }

    /// <summary>The topic the team is talking through right now; only meaningful in Discuss.</summary>
    public Guid? DiscussedCardId { get; set; }
    public RetroCard? DiscussedCard { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public List<RetroSection> Sections { get; set; } = [];
    public List<RetroParticipant> Participants { get; set; } = [];
}

public enum RetroPhase
{
    Collect = 0,
    Vote = 1,
    Discuss = 2,
    Group = 3,
    Actions = 4,
}
