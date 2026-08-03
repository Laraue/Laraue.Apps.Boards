namespace Laraue.Apps.Boards.DataAccess.Models;

public class OrganizationLog
{
    public long Id { get; set; }
    
    public long? EntityId { get; set; }
    public LogEntityType EntityType { get; set; }
    
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    
    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }

    public DateTime CreatedAt { get; set; }
    
    public LogAction Action { get; set; }
    
    public List<OrganizationLogItem>? Items { get; set; }
}

public enum LogEntityType
{
    Issue,
    Comment,
}

public enum LogAction
{
    Create,
    Update,
    Delete,
}