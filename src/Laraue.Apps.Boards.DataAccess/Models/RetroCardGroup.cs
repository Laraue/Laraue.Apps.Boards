using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// Several notes saying the same thing, merged into one topic so they stop splitting the votes.
/// The notes themselves are never rewritten or deleted - they keep their text and their author.
/// </summary>
public class RetroCardGroup
{
    public long Id { get; set; }

    public long RetroId { get; set; }
    public Retro? Retro { get; set; }

    /// <summary>Headline the team gives the topic; empty until somebody names it.</summary>
    [MaxLength(4096)]
    public required string Title { get; set; }

    public List<RetroCard> Cards { get; set; } = [];
}
