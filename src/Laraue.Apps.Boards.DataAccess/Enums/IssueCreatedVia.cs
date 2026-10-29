namespace Laraue.Apps.Boards.DataAccess.Enums;

/// <summary>
/// How an issue was created. The values are stored in the database, so an existing value never changes its number.
/// </summary>
public enum IssueCreatedVia
{
    /// <summary>
    /// In the web app (or Mini App) by a person.
    /// </summary>
    Web = 0,

    /// <summary>
    /// From a Telegram message (the issue has a <c>TelegramMessageId</c>).
    /// </summary>
    Telegram = 1,

    /// <summary>
    /// Through MCP with an API key made by hand.
    /// </summary>
    Mcp = 2,

    /// <summary>
    /// Through MCP with an API key issued by agent sign-in (the key has an <c>AgentSignInRequest</c>).
    /// </summary>
    Agent = 3,
}
