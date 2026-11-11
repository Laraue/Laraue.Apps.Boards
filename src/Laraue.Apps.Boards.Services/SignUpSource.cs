using System.Text;
using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Where a user came to Boards from (BRD-295): the frontend's <c>ref</c>/<c>utm_source</c>/referring site or
/// <c>invite</c>, a bot <c>/start</c> parameter, a Mini App <c>start_param</c>. Normalized the same way as
/// Laraue.Apps.Identity's <c>SignUpSource</c>, so the Boards copy (<see cref="User.SignUpSource"/>) and the one Identity
/// keeps for the Boards service are the same value.
/// </summary>
public static class SignUpSource
{
    /// <summary>
    /// Lower-case, only <c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>-</c> and <c>_</c> (anything else dropped), cut to
    /// <see cref="User.SignUpSourceMaxLength"/>. Null when nothing is left.
    /// </summary>
    public static string? Normalize(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        var builder = new StringBuilder(Math.Min(source.Length, User.SignUpSourceMaxLength));
        foreach (var c in source.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' or '_')
            {
                builder.Append(c);
                if (builder.Length == User.SignUpSourceMaxLength)
                    break;
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>
    /// The parameter of a bot deep link (<c>t.me/&lt;bot&gt;?start=producthunt</c> arrives as <c>/start producthunt</c>),
    /// normalized; null for any other text.
    /// </summary>
    public static string? FromStartCommand(string? messageText)
    {
        const string command = "/start ";
        return messageText?.StartsWith(command, StringComparison.Ordinal) == true
            ? Normalize(messageText[command.Length..])
            : null;
    }
}
