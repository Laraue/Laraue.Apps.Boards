namespace Laraue.Apps.Boards.Services.Members;

/// <summary>
/// A person on a row that isn't itself a response DTO - e.g. <c>Assignee</c> on an issue list row, a
/// name written into an issue's history, a row mapped into an immutable record: queries set
/// <see cref="UserId"/>, <see cref="IMemberProfileReader"/> fills the rest.
/// </summary>
public sealed class EnrichableUser : IEnrichableUser
{
    public required Guid UserId { get; init; }
    public string DisplayName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}
