namespace Laraue.Apps.Boards.DataAccess.Models;

public class OrganizationLog
{
    public long Id { get; set; }
    
    /// <summary>
    /// The issue link. The issue can be already deleted.
    /// </summary>
    public long? IssueId { get; set; }

    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    
    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }

    public DateTime CreatedAt { get; set; }
    
    public List<OrganizationLogItem>? Items { get; set; }
}