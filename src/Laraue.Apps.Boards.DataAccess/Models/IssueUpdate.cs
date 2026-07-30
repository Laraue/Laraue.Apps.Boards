namespace Laraue.Apps.Boards.DataAccess.Models;

public class IssueUpdate
{
    public long Id { get; set; }
    
    public long IssueId { get; set; }
    public Issue? Issue { get; set; }
    
    public DateTime CreatedAt { get; set; }
}