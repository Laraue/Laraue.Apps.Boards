namespace Laraue.Apps.Boards.Common;

public static class AuthSchemas
{
    public const string User = "User";
    public const string Organization = "Organization";

    /// <summary>
    /// A long-lived API key (see <c>ApiKey</c>/<c>ICoreApiKeysService</c>) instead of a short-lived
    /// JWT - used by machine callers like the MCP host, never by the browser/Mini App frontend.
    /// </summary>
    public const string ApiKey = "ApiKey";
}
