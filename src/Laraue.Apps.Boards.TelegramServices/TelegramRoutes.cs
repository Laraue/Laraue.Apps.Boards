namespace Laraue.Apps.Boards.TelegramServices;

public static class TelegramRoutes
{
    public const string LinkCommand = "/link(.*)";
    public const string UnlinkCommand = "/unlink(.*)";
    public const string SaveCommand = "/save(.*)";
    public const string AiSaveCommand = "/aisave(.*)";
    public const string InfoCommand = "/info(.*)";
    public const string DeleteCommand = "/delete(.*)";
    
    public const string DeleteConfirm = $"/delete/confirm/{{{Parameters.IssueId}}}";
    public const string Unlink = "/link/unlink";
    public const string LinkOrganization = $"/link/organization/{{{Parameters.Id}}}";
    public const string LinkSpace = $"/link/space/{{{Parameters.Id}}}";
    public const string LinkEpic = $"/link/epic/{{{Parameters.Id}}}";
    public const string LinkStatus = $"/link/status/{{{Parameters.Id}}}";
    public const string LinkSaveMode = $"/link/save-mode/{{{Parameters.StatusId}}}/{{{Parameters.Mode}}}";
    public const string LinkReplyMode =
        $"/link/reply-mode/{{{Parameters.StatusId}}}/{{{Parameters.SaveMode}}}/{{{Parameters.ReplyMode}}}";
    public const string BackToLink = "/link/back";
    public const string LinkSettingsSaveMode = $"/link/settings/save-mode/{{{Parameters.Mode}}}";
    public const string LinkSettingsReplyMode = $"/link/settings/reply-mode/{{{Parameters.Mode}}}";
    public const string LinkChangeDestination = "/link/destination";
    
    public const string CloseCallbackWindow = "/close-callback";

    /// <summary>
    /// Path parameter names of the routes above, for building their callback data with
    /// <c>CallbackRoutePath.WithPathParameter</c>. A controller action binds each one by its C# parameter name, so
    /// that name must stay equal to the constant.
    /// </summary>
    public static class Parameters
    {
        public const string Id = "id";
        public const string IssueId = "issueId";
        public const string StatusId = "statusId";
        public const string Mode = "mode";
        public const string SaveMode = "saveMode";
        public const string ReplyMode = "replyMode";
    }
}