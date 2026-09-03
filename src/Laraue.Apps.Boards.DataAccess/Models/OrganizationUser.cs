using Laraue.Apps.Boards.DataAccess.Enums;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class OrganizationUser
{
    public long Id { get; set; }
    
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public AdminAccessLevel AdminAccessLevel { get; set; }
    
    public bool CanRead { get; set; }
    public bool CanCreateRetros { get; set; }
    public bool CanCreateSpaces { get; set; }
    public bool CanUpdateSpaces { get; set; }
    public bool CanDeleteSpaces { get; set; }
    public bool CanCreateEpics { get; set; }
    public bool CanUpdateEpics { get; set; }
    public bool CanDeleteEpics { get; set; }
    public bool CanCreateIssues { get; set; }
    public bool CanUpdateIssues { get; set; }
    public bool CanDeleteIssues { get; set; }
}
