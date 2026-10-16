namespace Laraue.Apps.Boards.DataAccess.Enums;

/// <summary>
/// What a status means regardless of how a board names it. The values are stored in the database and
/// MCP clients pass them as statusCategoryId, so an existing value never changes its number.
/// </summary>
public enum StatusCategory
{
    /// <summary>
    /// Work on the issue hasn't started yet.
    /// </summary>
    Created = 0,

    /// <summary>
    /// The issue is being worked on.
    /// </summary>
    InProgress = 1,

    /// <summary>
    /// The issue is finished.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// The work is done and waits for someone to check it. Not finished yet: it counts as open, like
    /// <see cref="InProgress"/>.
    /// </summary>
    Review = 3,
}
