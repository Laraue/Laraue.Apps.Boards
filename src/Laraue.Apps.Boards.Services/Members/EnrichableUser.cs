namespace Laraue.Apps.Boards.Services.Members;

/// <summary>
/// A plain <see cref="IEnrichableUser"/> for code that needs a person's profile but has no DTO of its own
/// to put it on (e.g. a name written into an issue's history, a row mapped into an immutable record).
/// </summary>
public sealed class EnrichableUser(Guid userId) : IEnrichableUser
{
    public Guid UserId { get; } = userId;
    public string DisplayName { get; private set; } = string.Empty;
    public string Initials { get; private set; } = string.Empty;
    public string Color { get; private set; } = string.Empty;

    public void Enrich(MemberProfile profile)
    {
        DisplayName = profile.DisplayName;
        Initials = profile.Initials;
        Color = profile.Color;
    }
}
