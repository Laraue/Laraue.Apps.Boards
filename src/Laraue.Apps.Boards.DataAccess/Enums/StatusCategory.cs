namespace Laraue.Apps.Boards.DataAccess.Enums;

/// <summary>
/// What a status means regardless of how a board names it.
/// </summary>
public enum StatusCategory
{
    /// <summary>
    /// Work on the issue hasn't started yet.
    /// </summary>
    Created,

    /// <summary>
    /// The issue is being worked on.
    /// </summary>
    InProgress,

    /// <summary>
    /// The issue is finished.
    /// </summary>
    Completed,
}
