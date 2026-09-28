namespace Laraue.Apps.Boards.Services;

public static class UserInitials
{
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
