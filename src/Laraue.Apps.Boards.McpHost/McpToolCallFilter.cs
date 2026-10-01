using System.Diagnostics;
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
/// It also records every call's duration and outcome per tool (<see cref="McpToolMetrics"/>) - the one
/// place that sees both the tool name and how the call ended.
/// </summary>
public sealed class McpToolCallFilter
{
    private McpToolCallFilter()
    {
    }

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        return async (context, cancellationToken) =>
        {
            var metrics = context.Services?.GetService<McpToolMetrics>();
            var startedAt = Stopwatch.GetTimestamp();
            var status = McpToolMetrics.StatusUnhandled;

            try
            {
                // The SDK reports a missing or malformed argument as a bare "An error occurred invoking ...",
                // so check them against the tool's schema first and fail like any other bad request.
                if (context.MatchedPrimitive is McpServerTool tool)
                {
                    var errors = McpArgumentValidator.Validate(tool.ProtocolTool.InputSchema, context.Params?.Arguments);
                    if (errors.Count > 0)
                    {
                        throw new BadRequestException(errors);
                    }
                }

                var result = await next(context, cancellationToken);
                status = McpToolMetrics.StatusOk;

                return result;
            }
            catch (HttpException exception)
            {
                status = ((int)exception.StatusCode).ToString();

                context.Services?.GetService<ILogger<McpToolCallFilter>>()?.Log(
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
            finally
            {
                metrics?.RecordToolCall(context.Params?.Name, status, Stopwatch.GetElapsedTime(startedAt));
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
