namespace Laraue.Apps.Boards.DataAccess.Models;

public class IssueUpdateItem
{
    public long Id { get; set; }
    
    public long IssueUpdateId { get; set; }
    public IssueUpdate? IssueUpdate { get; set; }
    
    /// <summary>
    /// Value identifier (long / GUID serialized as string).
    /// </summary>
    public string? OldValueId { get; set; }
    
    /// <summary>
    /// Value identifier (long / GUID serialized as string).
    /// </summary>
    public string? NewValueId { get; set; }
    
    public ChangeAction Action { get; set; }
    public IssueUpdateEntityType EntityType { get; set; }
    
    public string? OldDisplayValue { get; set; }
    public string? NewDisplayValue { get; set; }
}

// Attachment added / removed. Old
// Content edited
// Comment created / edited / deleted
// Property edited
// Link attached / edited / detached
// Issue created

public enum IssueUpdateEntityType
{
    Attachment,
    Content,
    Comment,
    Property,
    Issue,
}

public enum ChangeAction
{
    Create,
    Update,
    Delete,
}

// { id: 1, createdAt: '2020-01-01 23:00:00', changes: [ { propertyType: 'attribute', action: 'delete', oldValue: 'Type: Bug' }, { propertyType: 'comment', action: 'edit', oldValue: 'Old comment', newValue: 'NewComment' } ] }