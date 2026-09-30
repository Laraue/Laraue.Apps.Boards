using System.Text.Json.Serialization;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.Members;

namespace Laraue.Apps.Boards.Services.History;

public record OrganizationHistoryItem
{
    public required DateTime CreatedAt { get; set; }
    public required UserDetails Owner { get; set; }
    public required string? ApiKeyName { get; set; }
    public required HistoryItemChange[] Changes { get; set; }
    public required LogEntityType EntityType { get; set; }
    public required LogAction Action { get; set; }
    public required string? IssueKey { get; set; }
    public required string? IssueTitle { get; set; }
}

[JsonDerivedType(typeof(IssueHistoryContentChange), "content")]
[JsonDerivedType(typeof(IssueHistoryTitleChange), "title")]
[JsonDerivedType(typeof(IssueHistoryAssigneeChange), "assignee")]
[JsonDerivedType(typeof(IssueHistoryStatusChange), "status")]
[JsonDerivedType(typeof(IssueHistoryPropertyChange), "property")]
[JsonDerivedType(typeof(IssueHistoryAttachmentChange), "attachment")]
[JsonDerivedType(typeof(IssueHistoryEpicChange), "epic")]
[JsonDerivedType(typeof(IssueHistorySpaceChange), "space")]
public abstract record HistoryItemChange
{
}

public record IssueHistoryContentChange : HistoryItemChange
{
    public required string? OldContent { get; set; }
    public required string? NewContent { get; set; }
}

public record IssueHistoryTitleChange : HistoryItemChange
{
    public required string? OldTitle { get; set; }
    public required string? NewTitle { get; set; }
}

public record IssueHistoryAssigneeChange : HistoryItemChange
{
    public required string? OldAssigneeDisplayName { get; set; }
    public required string? OldAssigneeColor { get; set; }
    public required string? NewAssigneeDisplayName { get; set; }
    public required string? NewAssigneeColor { get; set; }
}

public record IssueHistoryStatusChange : HistoryItemChange
{
    public required string? OldStatusName { get; set; }
    public required string? OldStatusColor { get; set; }
    public required string? NewStatusName { get; set; }
    public required string? NewStatusColor { get; set; }
}

public record IssueHistoryPropertyChange : HistoryItemChange
{
    public required string PropertyName { get; set; }
    public required AttributeType AttributeType { get; set; }
    public required string? OldValueName { get; set; }
    public required string? OldValueColor { get; set; }
    public required string? NewValueName { get; set; }
    public required string? NewValueColor { get; set; }
}

public record IssueHistoryAttachmentChange : HistoryItemChange
{
    public required string? FileName { get; set; }
    public required Guid? PreviewFileId { get; set; }
    public required AttachmentAction Action { get; set; }
}

public enum AttachmentAction
{
    Created,
    Deleted,
}

public record IssueHistoryEpicChange : HistoryItemChange
{
    public required string? OldEpicName { get; set; }
    public required string? OldEpicColor { get; set; }
    public required string? NewEpicName { get; set; }
    public required string? NewEpicColor { get; set; }
}

public record IssueHistorySpaceChange : HistoryItemChange
{
    public required string? OldSpaceName { get; set; }
    public required string? OldSpaceColor { get; set; }
    public required string? NewSpaceName { get; set; }
    public required string? NewSpaceColor { get; set; }
}
