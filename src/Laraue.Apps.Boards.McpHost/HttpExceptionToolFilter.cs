using System.Net;
using Laraue.Core.Exceptions.Web;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Laraue.Apps.Boards.McpHost;

/// <summary>
/// The MCP counterpart of Laraue.Core's <c>ExceptionHandleMiddleware</c> in WebApiHost. Middleware can't
/// do this here: a tool's exception never leaves the MCP request (it's HTTP 200 either way), and the SDK
/// turns any exception into a bare "An error occurred invoking '...'" - dropping the reason - and logs it
/// as an unhandled error. This filter turns our expected <see cref="HttpException"/>s (not found,
/// forbidden, bad request, ...) into an error result carrying the message and field errors, so the
/// client can tell what went wrong and correct itself. Any other exception still takes the SDK's path.
/// </summary>
public sealed class HttpExceptionToolFilter
{
    private HttpExceptionToolFilter()
    {
    }

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        return async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken);
            }
            catch (HttpException exception)
            {
                context.Services?.GetService<ILogger<HttpExceptionToolFilter>>()?.Log(
                    exception.StatusCode >= HttpStatusCode.InternalServerError ? LogLevel.Warning : LogLevel.Information,
                    "Tool {ToolName} returned {StatusCode}: {Message}",
                    context.Params?.Name,
                    (int)exception.StatusCode,
                    exception.Message);

                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = ToText(exception) }],
                };
            }
        };
    }

    private static string ToText(HttpException exception)
    {
        var text = $"{exception.StatusCode}: {exception.Message}";
        if (exception is not HttpExceptionWithErrors { Errors.Count: > 0 } withErrors)
        {
            return text;
        }

        var errors = withErrors.Errors.Select(x => $"- {x.Key}: {string.Join(" ", x.Value)}");

        return $"{text}{Environment.NewLine}{string.Join(Environment.NewLine, errors)}";
    }
}
