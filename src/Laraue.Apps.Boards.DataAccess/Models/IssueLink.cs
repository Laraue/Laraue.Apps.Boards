using Laraue.Apps.Boards.DataAccess.Enums;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// A link between two issues of the same organization, read as "source [type] target" (e.g. "source
/// blocks target"). Two issues have at most one link between them, whatever its type and direction -
/// changing the type is a removal and a new link. Not soft-deletable: removing a link deletes the row,
/// and the issues' history keeps it. A soft-deleted issue's links stay in the table and the reads hide
/// them.
/// </summary>
public class IssueLink
{
    public long Id { get; set; }

    public IssueLinkType Type { get; set; }

    public long SourceIssueId { get; set; }
    public Issue? SourceIssue { get; set; }

    public long TargetIssueId { get; set; }
    public Issue? TargetIssue { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
}
