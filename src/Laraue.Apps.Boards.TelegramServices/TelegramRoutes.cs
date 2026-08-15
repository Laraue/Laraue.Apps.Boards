namespace Laraue.Apps.Boards.TelegramServices;

public static class TelegramRoutes
{
    public const string LinkCommand = "/link";
    public const string UnlinkCommand = "/unlink";
    
    public const string ChangeLink = "/link/change";
    public const string Unlink = "/link/unlink";
    public const string LinkOrganization = "/link/organization/{id}";
    public const string LinkSpace = "/link/space/{id}";
    
    public const string CloseCallbackWindow = "/close-callback";
}