namespace Laraue.Apps.Boards.DataAccess.Models;

public class RetroCardVote
{
    public Guid CardId { get; set; }
    public RetroCard? Card { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }
}
