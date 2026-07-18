namespace Laraue.Apps.Boards.Services;

public static class UserInitialsUtility
{
    public static UserInitials GetInitials(
        string? username,
        string? firstName,
        string? lastName)
    {
        var displayName = username;
        var initial = displayName?.Length > 1 ? displayName[..2] : "";

        if (displayName is null)
        {
            if (firstName?.Length > 0 && lastName?.Length > 0)
            {
                displayName = $"{firstName} {lastName}";
                initial = $"{firstName[0]}{lastName[0]}";
            }
            else if (firstName?.Length > 1)
            {
                displayName = firstName;
                initial = firstName[..1];
            }
            else if (lastName?.Length > 1)
            {
                displayName = lastName;
                initial = lastName[..1];
            }
            else
            {
                displayName = "Unknown";
                initial = "UN";
            }
        }

        return new UserInitials
        {
            DisplayName = displayName,
            Initials = initial
        };
    }
}

public class UserInitials
{
    public required string DisplayName { get; set; }
    public required string Initials { get; set; }
}
