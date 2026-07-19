namespace Laraue.Apps.Boards.WebApiHost;

public static class AuthCookies
{
    public const string User = "boards_user";
    public const string Organization = "boards_organization";

    public static void Append(HttpResponse response, string name, string token, IHostEnvironment environment)
    {
        response.Cookies.Append(name, token, Options(environment));
    }

    public static void Delete(HttpResponse response, IHostEnvironment environment)
    {
        var options = Options(environment);
        response.Cookies.Delete(User, options);
        response.Cookies.Delete(Organization, options);
    }

    private static CookieOptions Options(IHostEnvironment environment) => new()
    {
        HttpOnly = true,
        IsEssential = true,
        Path = "/",
        SameSite = SameSiteMode.Lax,
        Secure = !environment.IsDevelopment(),
        MaxAge = TimeSpan.FromDays(365),
    };
}
