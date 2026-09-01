using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class RetroCard
{
    public Guid Id { get; set; }

    public long SectionId { get; set; }
    public RetroSection? Section { get; set; }

    public Guid AuthorId { get; set; }
    public User? Author { get; set; }

    /// <summary>Who owns the action; only cards of the actions section ever set it.</summary>
    public Guid? AssigneeId { get; set; }
    public User? Assignee { get; set; }

    [MaxLength(4096)]
    public required string Text { get; set; }

    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>Paint order inside the retro; the card moved last sits on top for everyone.</summary>
    public int StackOrder { get; set; }
    public bool Done { get; set; }
    public bool Revealed { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<RetroCardVote> Votes { get; set; } = [];
}
