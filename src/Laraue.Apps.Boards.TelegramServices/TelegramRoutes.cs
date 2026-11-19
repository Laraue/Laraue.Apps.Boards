namespace Laraue.Apps.Boards.TelegramServices;

public static class TelegramRoutes
{
    public const string LinkCommand = $"{Commands.Link}(.*)";
    public const string UnlinkCommand = $"{Commands.Unlink}(.*)";
    public const string SaveCommand = $"{Commands.Save}(.*)";
    public const string AiSaveCommand = $"{Commands.AiSave}(.*)";
    public const string InfoCommand = $"{Commands.Info}(.*)";
    public const string DeleteCommand = $"{Commands.Delete}(.*)";
    
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
        $"/save/where/{{{Parameters.MessageId}}}/{{{Parameters.Summarize}}}/{{{Parameters.DestinationType}}}/{{{Parameters.DestinationId}}}";

    public const string CloseCallbackWindow = "/close-callback";

    /// <summary>
    /// The bot's commands as typed, for the command routes above and for code that reads a command's text.
    /// </summary>
    public static class Commands
    {
        public const string Link = "/link";
        public const string Unlink = "/unlink";
        public const string Save = "/save";
        public const string AiSave = "/aisave";
        public const string Info = "/info";
        public const string Delete = "/delete";
    }

    /// <summary>
    /// Path parameter names of the routes above: callback data is built with <c>WithPathParameter</c> and controller
    /// actions bind with <c>[FromPath(...)]</c>, both by these names.
    /// </summary>
    public static class Parameters
    {
        public const string Id = "id";
        public const string MessageId = "messageId";
        public const string Summarize = "summarize";
        public const string IssueId = "issueId";
        public const string DestinationType = "destinationType";
        public const string DestinationId = "destinationId";
        public const string Mode = "mode";
        public const string SaveMode = "saveMode";
        public const string ReplyMode = "replyMode";
    }
}