namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Where a user came to Boards from (BRD-295). Boards doesn't clean the value: it sends what it received to
/// Laraue.Apps.Identity, which normalizes and stores it, and keeps the value Identity returns
/// (<c>User.SignUpSource</c>).
/// </summary>
public static class SignUpSource
{
    /// <summary>
    /// The parameter of a bot deep link (<c>t.me/&lt;bot&gt;?start=producthunt</c> arrives as <c>/start producthunt</c>),
    /// as is; null for any other text.
    /// </summary>
    public static string? FromStartCommand(string? messageText)
    {
        const string command = "/start ";
        if (messageText?.StartsWith(command, StringComparison.Ordinal) != true)
            return null;

        var parameter = messageText[command.Length..].Trim();
        return parameter.Length == 0 ? null : parameter;
    }
}
