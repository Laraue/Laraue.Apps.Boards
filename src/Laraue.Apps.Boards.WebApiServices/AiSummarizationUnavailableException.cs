using System.Net;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Boards.WebApiServices;

/// <summary>
/// Thrown when the AI summarization dependency (<see cref="Services.Ai.IAiContentSummarizer"/>)
/// fails - maps to 503 since it's a downstream/AI-provider failure, not a client mistake.
/// </summary>
public class AiSummarizationUnavailableException(string message)
    : HttpException(HttpStatusCode.ServiceUnavailable, message);
