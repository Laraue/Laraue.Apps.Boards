namespace Laraue.Apps.Retro.WebApiHost;

/// <summary>
/// Only the organization cookie name is needed here - retro is org-scoped only, unlike Boards
/// which also has a user-level session. Must stay equal to
/// <c>Laraue.Apps.Boards.WebApiHost.AuthCookies.Organization</c>: it's the same cookie, set by
/// Boards' login flow and read here too, since a browser session logs into Boards, not Retro
/// directly.
/// </summary>
public static class AuthCookies
{
    public const string Organization = "boards_organization";
}
