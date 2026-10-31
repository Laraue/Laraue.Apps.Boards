using Laraue.Apps.Boards.DataAccess.Enums;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// What the sample project of the setup window (BRD-293) is made of: a space with one board and a few issues
/// showing statuses, a comment and a blocking link. English only.
/// </summary>
public static class SampleProjectContent
{
    public const string SpaceName = "Sample project";

    /// <summary>
    /// Keys tried in order for the sample space; the first one the organization doesn't use wins.
    /// </summary>
    public static readonly string[] SpaceKeys = ["SMP", "DMO", "EXM", "TRY"];

    public const string SpaceDescription =
        "A sample project added when you set up Boards. Look around, move the cards, change anything - "
        + "and delete this space in its settings when you no longer need it.";

    public const string BoardName = "Website launch";

    public static readonly Status[] BoardStatuses =
    [
        new() { Name = "To Do", Color = "#79c0ff", Category = StatusCategory.Created },
        new() { Name = "In Progress", Color = "#e3b341", Category = StatusCategory.InProgress },
        new() { Name = "Review", Color = "#a371f7", Category = StatusCategory.Review },
        new() { Name = "Done", Color = "#3fb950", Category = StatusCategory.Completed },
    ];

    /// <summary>
    /// The board's issues in the order they're created (and shown), each in the board status at
    /// <see cref="SampleIssue.StatusIndex"/>; a null index puts it in the space's backlog instead.
    /// </summary>
    public static readonly SampleIssue[] Issues =
    [
        new(
            "Welcome to Boards: start here",
            """
            This space is a sample project, so you can see how Boards works before adding your own work.

            - **Board columns are statuses.** Drag a card to another column to change its status.
            - **Open a card** to edit it, assign it, comment on it and see its history.
            - **Cards can block each other.** "Write the launch announcement" waits for the landing page copy.
            - **Save from Telegram.** Link a chat in the organization settings and messages become cards.

            When you're done, delete this space in its settings.
            """,
            StatusIndex: 0),
        new(
            "Write the landing page copy",
            "Headline, three benefits and a call to action. Keep it short enough to read on a phone.",
            StatusIndex: 1,
            Comment: "The first draft is ready. Waiting for feedback on the headline."),
        new(
            "Review the pricing page",
            "Check that the plan limits match what the app enforces.",
            StatusIndex: 2),
        new(
            "Pick the color palette",
            "Two main colors and one accent, checked for contrast in light and dark themes.",
            StatusIndex: 3),
        new(
            "Set up the domain and HTTPS",
            null,
            StatusIndex: 3),
        new(
            "Write the launch announcement",
            "A short post for the blog and social networks. Starts when the landing page copy is done.",
            StatusIndex: 0,
            BlockedByIndex: 1),
        new(
            "Idea: add an FAQ section",
            "Not planned yet - ideas wait in the backlog until they get a board.",
            StatusIndex: null),
    ];
}

/// <param name="StatusIndex">Index in <see cref="SampleProjectContent.BoardStatuses"/>, or null for the backlog.</param>
/// <param name="BlockedByIndex">Index in <see cref="SampleProjectContent.Issues"/> of the issue that blocks this one.</param>
public sealed record SampleIssue(
    string Title,
    string? Content,
    int? StatusIndex,
    string? Comment = null,
    int? BlockedByIndex = null);
