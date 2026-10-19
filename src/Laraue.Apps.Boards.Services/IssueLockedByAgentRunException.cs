using System.Net;
using Laraue.Apps.Boards.Services.Resources;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// The issue has an open agent run (Ready or Working), so its fields can't be edited and it can't be deleted
/// (BRD-305): the agent works from what the assignee handed off. Cancelling the run unlocks the issue. Status
/// changes and comments stay allowed. A 409, so the message reaches REST and MCP callers as is; the Telegram bot
/// catches it and answers with its own phrase.
/// </summary>
public class IssueLockedByAgentRunException()
    : HttpException(HttpStatusCode.Conflict, ErrorMessages.IssueLockedByAgentRun);
