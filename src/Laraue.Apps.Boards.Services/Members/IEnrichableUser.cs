namespace Laraue.Apps.Boards.Services.Members;

/// <summary>
/// A DTO showing a person - queries project just <see cref="UserId"/>, and
/// <see cref="IMemberProfileReader"/> sets how that person is shown in the organization. A DTO with
/// differently named properties, or one that doesn't show some of them, maps them with an explicit
/// implementation.
/// </summary>
public interface IEnrichableUser
{
    Guid UserId { get; }
    string DisplayName { set; }
    string Initials { set; }
    string Color { set; }
}
