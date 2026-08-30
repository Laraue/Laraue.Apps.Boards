using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class RetroSection
{
    public long Id { get; set; }

    public long RetroId { get; set; }
    public Retro? Retro { get; set; }

    [MaxLength(128)]
    public required string Name { get; set; }

    [MaxLength(7)]
    public required string Color { get; set; }

    public int SortOrder { get; set; }

    public List<RetroCard> Cards { get; set; } = [];
}
