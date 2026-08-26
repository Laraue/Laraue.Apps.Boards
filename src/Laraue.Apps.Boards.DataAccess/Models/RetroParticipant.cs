namespace Laraue.Apps.Boards.DataAccess.Models;

public class RetroParticipant
{
    public long RetroId { get; set; }
    public Retro? Retro { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }
}
