namespace Laraue.Apps.Boards.Services.Members;

/// <summary>
/// How a member is shown in an organization (<c>OrganizationUser.DisplayName/Initials/Color</c>).
/// </summary>
public sealed record MemberProfile(string DisplayName, string Initials, string Color)
{
    /// <summary>
    /// Shown for a user with no membership row in the organization - only possible for data from before
    /// rows were kept for members who left.
    /// </summary>
    public static readonly MemberProfile Unknown = new("Unknown", "UN", Palette.FirstColor);
}
