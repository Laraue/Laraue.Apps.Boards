namespace Laraue.Apps.Boards.DataAccess.Models;

public class IssueChange
{
    public long Id { get; set; }
    
    public long IssueId { get; set; }
    public Issue? Issue { get; set; }
    
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}