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
    
    /// <summary>
    /// Holds an issue's whole content for <see cref="PropertyType.Content"/>, so it's as long as the content.
    /// </summary>
    [MaxLength(Constraints.MaxContentLength)]
    public string? OldDisplayValue { get; set; }

    /// <inheritdoc cref="OldDisplayValue"/>
    [MaxLength(Constraints.MaxContentLength)]
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
    AgentRun = 8,

    /// <summary>
    /// A link to another issue was added or removed (<see cref="IssueLink"/>). Written to both issues:
    /// <see cref="OrganizationLogItem.PropertyName"/> is the link's meaning from this issue's side (an
    /// <c>IssueLinkRelation</c> name, e.g. BlockedBy), the new (added) or old (removed) value id/display
    /// value the other issue's id/key.
    /// </summary>
    IssueLink = 9,
}