using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class RetroCard
{
    public Guid Id { get; set; }

    public long SectionId { get; set; }
    public RetroSection? Section { get; set; }

    public Guid AuthorId { get; set; }
    public User? Author { get; set; }

    [MaxLength(4096)]
    public required string Text { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public bool Done { get; set; }
    public bool Revealed { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<RetroCardVote> Votes { get; set; } = [];
}
