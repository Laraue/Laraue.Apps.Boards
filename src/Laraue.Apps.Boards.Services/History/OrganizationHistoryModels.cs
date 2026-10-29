using System.Text.Json.Serialization;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.Members;

namespace Laraue.Apps.Boards.Services.History;

public record OrganizationHistoryItem
{
    public required DateTime CreatedAt { get; set; }
    public required UserDetails Owner { get; set; }
    public required string? ApiKeyName { get; set; }

    /// <summary>
    /// Agent when the change was made with a key issued by agent sign-in, so it reads as the agent's on behalf of
    /// <see cref="Owner"/>; User otherwise (in the app, or with a key made by hand).
    /// </summary>
    public required AuthorKind AuthorKind { get; set; }

    /// <summary>
    /// The agent's client and machine, set when <see cref="AuthorKind"/> is Agent.
    /// </summary>
    public required AgentDetails? Agent { get; set; }

    public required HistoryItemChange[] Changes { get; set; }
    public required LogEntityType EntityType { get; set; }
    public required LogAction Action { get; set; }
    public required string? IssueKey { get; set; }
    public required string? IssueTitle { get; set; }
}

/// <summary>
/// Who an action reads as: the person, or an agent acting for them.
/// </summary>
public enum AuthorKind
{
    User,
    Agent,
}

/// <summary>
/// An agent signed in with agent sign-in: its own name (e.g. "Claude Code agent") and the machine it runs on.
/// </summary>
public record AgentDetails
{
    public required string ClientName { get; init; }
    public required string MachineName { get; init; }
}

/// <summary>
/// The API key a comment was written through, and the agent behind it when the key came from agent sign-in.
/// </summary>
public record CommentApiKey
{
    public required string Name { get; init; }
    public required AgentDetails? Agent { get; init; }
}

[JsonDerivedType(typeof(IssueHistoryContentChange), "content")]
[JsonDerivedType(typeof(IssueHistoryTitleChange), "title")]
[JsonDerivedType(typeof(IssueHistoryAssigneeChange), "assignee")]
[JsonDerivedType(typeof(IssueHistoryStatusChange), "status")]
[JsonDerivedType(typeof(IssueHistoryPropertyChange), "property")]
[JsonDerivedType(typeof(IssueHistoryAttachmentChange), "attachment")]
[JsonDerivedType(typeof(IssueHistoryEpicChange), "epic")]
[JsonDerivedType(typeof(IssueHistorySpaceChange), "space")]
[JsonDerivedType(typeof(IssueHistoryAgentRunChange), "agentRun")]
[JsonDerivedType(typeof(IssueHistoryLinkChange), "link")]
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
    public required StatusCategory? OldStatusCategory { get; set; }
    public required string? NewStatusName { get; set; }
    public required string? NewStatusColor { get; set; }
    public required StatusCategory? NewStatusCategory { get; set; }
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

/// <summary>
/// An agent run of the issue changed state (BRD-305) - <see cref="OldState"/> is null when the run was created
/// (the issue was handed to the agent).
/// </summary>
public record IssueHistoryAgentRunChange : HistoryItemChange
{
    public required long RunId { get; set; }
    public required AgentRunState? OldState { get; set; }
    public required AgentRunState NewState { get; set; }
}

/// <summary>
/// A link to another issue was added or removed. Only the other issue's key as it was then: the history is
/// read without permission checks, so it says nothing more about an issue the reader may not see.
/// </summary>
public record IssueHistoryLinkChange : HistoryItemChange
{
    /// <summary>What the link means from this issue's side.</summary>
    public required IssueLinkRelation Relation { get; set; }
    public required string? IssueKey { get; set; }
    public required IssueLinkAction Action { get; set; }
}

public enum IssueLinkAction
{
    Added,
    Removed,
}
