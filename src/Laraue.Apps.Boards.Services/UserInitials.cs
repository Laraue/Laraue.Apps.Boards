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
                initial = firstName[..2];
            }
            else if (lastName?.Length > 1)
            {
                displayName = lastName;
                initial = lastName[..2];
            }
            else
            {
                displayName = "Unknown";
                initial = "UN";
            }
        }

        DisplayName = displayName;
        Initials = initial.ToUpperInvariant();
    }

    public string DisplayName { get; }
    public string Initials { get; }

    /// <summary>
    /// Initials for a name the user typed themselves: the first letters of its first two words
    /// ("Ivan Petrov" -> "IP"), or the first two letters of a single word ("Ivan" -> "IV").
    /// </summary>
    public static string FromDisplayName(string displayName)
    {
        var words = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var initials = words.Length switch
        {
            0 => "",
            1 => words[0].Length > 1 ? words[0][..2] : words[0],
            _ => $"{words[0][0]}{words[1][0]}",
        };

        return initials.ToUpperInvariant();
    }
}