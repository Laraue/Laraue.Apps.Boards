namespace Laraue.Apps.Boards.DataAccess.Models;

public class DirectSpacePermission
{
    public long Id { get; set; }
    
    /// <summary>
    /// When space is not set than permission is applied for all spaces.
    /// </summary>
    public long SpaceId { get; set; }
    public Space? Space { get; set; }
    
    public long OrganizationUserId { get; set; }
    public OrganizationUser? OrganizationUser { get; set; }
    
    public bool CanRead { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }
    public bool CanManageRetros { get; set; }
    public bool CanCreateEpics { get; set; }
    public bool CanUpdateEpics { get; set; }
    public bool CanDeleteEpics { get; set; }
    public bool CanCreateIssues { get; set; }
    public bool CanUpdateIssues { get; set; }
    public bool CanDeleteIssues { get; set; }
}