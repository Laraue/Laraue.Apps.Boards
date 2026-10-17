using Laraue.Apps.Boards.DataAccess.Enums;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// What an <see cref="DataAccess.Models.IssueLink"/> means from one of its two issues' side - how the
/// issue shows the link, and how a caller asks for one ("this issue is blocked by BRD-12").
/// </summary>
public enum IssueLinkRelation
{
    /// <summary>This issue blocks the other one.</summary>
    Blocks,

    /// <summary>The other issue blocks this one.</summary>
    BlockedBy,

    /// <summary>The two issues are related.</summary>
    RelatesTo,

    /// <summary>This issue duplicates the other one (the original).</summary>
    Duplicates,

    /// <summary>The other issue duplicates this one: this is the original.</summary>
    DuplicatedBy,
}

/// <summary>
/// How a link is stored: <see cref="SourceIssueId"/> [<see cref="Type"/>] <see cref="TargetIssueId"/>.
/// </summary>
public readonly record struct StoredIssueLink(IssueLinkType Type, long SourceIssueId, long TargetIssueId);

public static class IssueLinkRelationExtensions
{
    /// <summary>
    /// How "<paramref name="issueId"/> [<paramref name="relation"/>] <paramref name="otherIssueId"/>" is
    /// stored. A <see cref="IssueLinkRelation.RelatesTo"/> link always has the lower id as its source.
    /// </summary>
    public static StoredIssueLink ToStored(this IssueLinkRelation relation, long issueId, long otherIssueId)
    {
        return relation switch
        {
            IssueLinkRelation.Blocks => new StoredIssueLink(IssueLinkType.Blocks, issueId, otherIssueId),
            IssueLinkRelation.BlockedBy => new StoredIssueLink(IssueLinkType.Blocks, otherIssueId, issueId),
            IssueLinkRelation.RelatesTo => new StoredIssueLink(
                IssueLinkType.RelatesTo,
                Math.Min(issueId, otherIssueId),
                Math.Max(issueId, otherIssueId)),
            IssueLinkRelation.Duplicates => new StoredIssueLink(IssueLinkType.Duplicates, issueId, otherIssueId),
            IssueLinkRelation.DuplicatedBy => new StoredIssueLink(IssueLinkType.Duplicates, otherIssueId, issueId),
            _ => throw new ArgumentOutOfRangeException(nameof(relation), relation, null),
        };
    }

    /// <summary>
    /// What a stored link of <paramref name="type"/> means from the side of its source
    /// (<paramref name="isSource"/>) or its target issue.
    /// </summary>
    public static IssueLinkRelation ToRelation(this IssueLinkType type, bool isSource)
    {
        return type switch
        {
            IssueLinkType.Blocks => isSource ? IssueLinkRelation.Blocks : IssueLinkRelation.BlockedBy,
            IssueLinkType.RelatesTo => IssueLinkRelation.RelatesTo,
            IssueLinkType.Duplicates => isSource ? IssueLinkRelation.Duplicates : IssueLinkRelation.DuplicatedBy,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }

    /// <summary>
    /// The relation as it reads between two issue keys, e.g. "BRD-1 is blocked by BRD-2".
    /// </summary>
    public static string ToDisplayText(this IssueLinkRelation relation)
    {
        return relation switch
        {
            IssueLinkRelation.Blocks => "blocks",
            IssueLinkRelation.BlockedBy => "is blocked by",
            IssueLinkRelation.RelatesTo => "relates to",
            IssueLinkRelation.Duplicates => "duplicates",
            IssueLinkRelation.DuplicatedBy => "is duplicated by",
            _ => throw new ArgumentOutOfRangeException(nameof(relation), relation, null),
        };
    }
}
