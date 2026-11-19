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
    public const string LinkChooseEachTime =
        $"/link/each-time/{{{Parameters.DestinationType}}}/{{{Parameters.DestinationId}}}";
    public const string LinkSaveMode =
        $"/link/save-mode/{{{Parameters.DestinationType}}}/{{{Parameters.DestinationId}}}/{{{Parameters.Mode}}}";
    public const string LinkReplyMode =
        $"/link/reply-mode/{{{Parameters.DestinationType}}}/{{{Parameters.DestinationId}}}/{{{Parameters.SaveMode}}}/{{{Parameters.ReplyMode}}}";
    public const string BackToLink = "/link/back";
    public const string LinkSettingsSaveMode = $"/link/settings/save-mode/{{{Parameters.Mode}}}";
    public const string LinkSettingsReplyMode = $"/link/settings/reply-mode/{{{Parameters.Mode}}}";
    public const string LinkChangeDestination = "/link/destination";
    
    public const string SaveDestinationPicked =
        $"/save/where/{{{Parameters.MessageId}}}/{{{Parameters.DestinationType}}}/{{{Parameters.DestinationId}}}";

    public const string CloseCallbackWindow = "/close-callback";

    /// <summary>
    /// Path parameter names of the routes above: callback data is built with <c>WithPathParameter</c> and controller
    /// actions bind with <c>[FromPath(...)]</c>, both by these names.
    /// </summary>
    public static class Parameters
    {
        public const string Id = "id";
        public const string MessageId = "messageId";
        public const string IssueId = "issueId";
        public const string DestinationType = "destinationType";
        public const string DestinationId = "destinationId";
        public const string Mode = "mode";
        public const string SaveMode = "saveMode";
        public const string ReplyMode = "replyMode";
    }
}