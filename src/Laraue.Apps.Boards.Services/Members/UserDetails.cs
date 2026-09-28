using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.Services.Members;

/// <summary>
/// A person as shown in an organization. Queries set just <see cref="UserId"/>;
/// <see cref="IMemberProfileReader"/> fills the rest. Used directly in responses (issue owner, comment
/// and history authors) and as the person on rows that aren't responses themselves (an issue list row's
/// assignee, a name written into history, an MCP record). The profile properties are
/// <see cref="RequiredAttribute"/> so the API schema keeps them required - they're always filled by the
/// time a response is sent.
/// </summary>
public record UserDetails : IEnrichableUser
{
    public required Guid UserId { get; init; }
    [Required]
    public string Color { get; set; } = string.Empty;

    [Required]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    public string Initials { get; set; } = string.Empty;
}
