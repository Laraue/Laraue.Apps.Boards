namespace Laraue.Apps.Boards.DataAccess.Enums;

/// <summary>
/// What a link between two issues means (<see cref="Models.IssueLink"/>). The values are stored in the
/// database, so an existing value never changes its number.
/// </summary>
public enum IssueLinkType
{
    /// <summary>
    /// The source issue blocks the target one: the target waits until the source is done. Directed, and
    /// the links of this type never form a cycle.
    /// </summary>
    Blocks = 0,

    /// <summary>
    /// The two issues are related. Symmetric: stored once, with the lower issue id as the source, and
    /// read the same way from both issues.
    /// </summary>
    RelatesTo = 1,

    /// <summary>
    /// The source issue duplicates the target one, which is the original. Directed.
    /// </summary>
    Duplicates = 2,
}
