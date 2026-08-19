namespace Laraue.Apps.Boards.TelegramServices;

public static class TelegramRoutes
{
    public const string LinkCommand = "/link";
    public const string UnlinkCommand = "/unlink";
    public const string SaveCommand = "/save(.*)";
    
    public const string Unlink = "/link/unlink";
    public const string LinkOrganization = "/link/organization/{id}";
    public const string LinkSpace = "/link/space/{id}";
    public const string LinkEpic = "/link/epic/{id}";
    public const string LinkStatus = "/link/status/{id}";
    public const string LinkSaveMode = "/link/save-mode/{statusId}/{mode}";
    public const string BackToLink = "/link/back";
    
    public const string CloseCallbackWindow = "/close-callback";
}