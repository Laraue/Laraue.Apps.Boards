using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class OrganizationLogItem
{
    public long Id { get; set; }
    
    public long OrganizationLogId { get; set; }
    public OrganizationLog? OrganizationLog { get; set; }
    
    public PropertyType PropertyType { get; set; }
    
    [MaxLength(36)]
    public string? ParentId { get; set; }
    
    [MaxLength(36)]
    public string? OldValueId { get; set; }
    
    [MaxLength(36)]
    public string? NewValueId { get; set; }
    
    [MaxLength(255)]
    public string? PropertyName { get; set; }
    
    [MaxLength(4096)]
    public string? OldDisplayValue { get; set; }
    
    [MaxLength(4096)]
    public string? NewDisplayValue { get; set; }
}

public enum PropertyType
{
    Attachment,
    Content,
    Attribute,
    Assignee,
    Status,
    Epic,
    Space,
    Title,

    /// <summary>
    /// An agent run of the issue changed state (<see cref="IssueAgentRun"/>): <see cref="OrganizationLogItem.ParentId"/>
    /// is the run id, the value ids/display values the old and new <c>AgentRunState</c> (number/name).
    /// </summary>
    AgentRun,
}