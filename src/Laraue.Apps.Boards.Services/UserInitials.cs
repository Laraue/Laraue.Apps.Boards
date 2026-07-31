namespace Laraue.Apps.Boards.Services;

public class UserInitials
{
    public UserInitials(
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
        
        DisplayName = displayName;
        Initials = initial;
    }

    public string DisplayName { get; }
    public string Initials { get; }
}

public class UserInitialsSource
{
    public required string? Username { get; set; }
    public required string? FirstName { get; set; }
    public required string? LastName { get; set; }
}