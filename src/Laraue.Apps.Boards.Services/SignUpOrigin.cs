namespace Laraue.Apps.Boards.Services;

/// <summary>
/// What Boards saw about where a signing-up user came from (BRD-295), passed raw to Laraue.Apps.Identity, which works out
/// and normalizes the source (its <c>SignUpSource.Resolve</c>) and returns it - <c>User.SignUpSource</c> keeps that value.
/// <see cref="Source"/> is an explicit source, which wins over the rest: a bot <c>/start</c> parameter, a Mini App
/// <c>start_param</c>, or <c>invite</c> for a landing on Boards' invite page.
/// </summary>
/// <param name="LandingUrl">The first page the user opened on the site, as the frontend saved it.</param>
/// <param name="Referrer">The site that sent them there (<c>document.referrer</c>), as the frontend saved it.</param>
public sealed record SignUpOrigin(string? Source, string? LandingUrl, string? Referrer)
{
    private const string InvitePathPrefix = "/join/";

    /// <summary>
    /// A web sign-up: the frontend's raw landing URL and referrer. A landing on the invite page makes the source
    /// <c>invite</c> - Boards' own route, so this rule lives here rather than in Identity.
    /// </summary>
    public static SignUpOrigin FromWeb(string? landingUrl, string? referrer)
    {
        var isInvite = Uri.TryCreate(landingUrl, UriKind.Absolute, out var landing)
            && landing.AbsolutePath.StartsWith(InvitePathPrefix, StringComparison.OrdinalIgnoreCase);

        return new SignUpOrigin(isInvite ? "invite" : null, landingUrl, referrer);
    }

    /// <summary>
    /// A bot sign-up: the parameter of a bot deep link (<c>t.me/&lt;bot&gt;?start=producthunt</c> arrives as
    /// <c>/start producthunt</c>), as is; no source for any other text.
    /// </summary>
    public static SignUpOrigin FromStartCommand(string? messageText)
    {
        const string command = "/start ";
        var parameter = messageText?.StartsWith(command, StringComparison.Ordinal) == true
            ? messageText[command.Length..].Trim()
            : null;

        return new SignUpOrigin(string.IsNullOrEmpty(parameter) ? null : parameter, null, null);
    }
}
