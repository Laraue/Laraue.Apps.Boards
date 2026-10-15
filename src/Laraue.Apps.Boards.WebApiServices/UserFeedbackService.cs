using System.ComponentModel.DataAnnotations;
using Grpc.Core;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Microsoft.Extensions.Logging;
using Contracts = Laraue.Apps.Feedback.Internal.Contracts;

namespace Laraue.Apps.Boards.WebApiServices;

public interface IUserFeedbackService
{
    /// <summary>
    /// Sends the user's feedback to Laraue.Apps.Feedback, with their global Laraue user id and the
    /// current organization (if any) added to what the frontend sent. Returns the stored feedback's id.
    /// </summary>
    Task<SendFeedbackResponse> Send(
        Guid userId,
        long? organizationId,
        SendFeedbackRequest request,
        CancellationToken cancellationToken);
}

public class UserFeedbackService(
    DatabaseContext context,
    Contracts.FeedbackService.FeedbackServiceClient feedbackClient,
    ILogger<UserFeedbackService> logger)
    : IUserFeedbackService
{
    /// <summary>
    /// The context key the current organization's id is sent under. Set by Boards only - a value the
    /// frontend sends under it is replaced, or dropped when there's no current organization.
    /// </summary>
    public const string OrganizationContextKey = "organization";

    public async Task<SendFeedbackResponse> Send(
        Guid userId,
        long? organizationId,
        SendFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        var globalUserId = await context.ActiveUsers()
            .Where(x => x.Id == userId)
            .Select(x => x.GlobalUserId)
            .FirstOrThrowNotFoundEFAsync("User is not found", cancellationToken);

        var grpcRequest = new Contracts.SubmitFeedbackRequest
        {
            UserId = globalUserId.ToString(),
            Type = ToContractType(request.Type!.Value),
            Message = request.Message,
        };

        if (!string.IsNullOrWhiteSpace(request.Contact))
        {
            grpcRequest.Contact = request.Contact.Trim();
        }

        if (request.Context is not null)
        {
            grpcRequest.Context.Add(request.Context);
        }

        grpcRequest.Context.Remove(OrganizationContextKey);
        if (organizationId is not null)
        {
            grpcRequest.Context[OrganizationContextKey] = organizationId.Value.ToString();
        }

        // Feedback validates the message and context limits itself: INVALID_ARGUMENT comes back from
        // Laraue.Grpc.Client as a BadRequestException (400), not as an RpcException.
        try
        {
            var response = await feedbackClient.SubmitFeedbackAsync(
                grpcRequest,
                cancellationToken: cancellationToken);

            return new SendFeedbackResponse { FeedbackId = response.FeedbackId };
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Could not send feedback of user {GlobalUserId}", globalUserId);
            throw new FeedbackServiceUnavailableException(ErrorMessages.FeedbackServiceUnavailable);
        }
    }

    private static Contracts.FeedbackType ToContractType(FeedbackType type)
    {
        return type switch
        {
            FeedbackType.Bug => Contracts.FeedbackType.Bug,
            FeedbackType.Idea => Contracts.FeedbackType.Idea,
            FeedbackType.Question => Contracts.FeedbackType.Question,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}

public enum FeedbackType
{
    Bug,
    Idea,
    Question,
}

public record SendFeedbackRequest
{
    [Required]
    public FeedbackType? Type { get; init; }

    /// <summary>
    /// The feedback text - at most 3000 characters once trimmed (checked by Laraue.Apps.Feedback).
    /// </summary>
    [Required]
    public required string Message { get; init; }

    /// <summary>
    /// How the user would like to be reached (an email, a Telegram username), when not through the
    /// account they signed in with - at most 256 characters once trimmed (checked by
    /// Laraue.Apps.Feedback). Null or blank means answering through the account is fine.
    /// </summary>
    public string? Contact { get; init; }

    /// <summary>
    /// Details from the frontend: page URL (<c>page</c>, shown in the team's Telegram notification),
    /// app version, locale, browser. At most 20 entries (including the <c>organization</c> one Boards
    /// adds), keys up to 64 and values up to 2048 characters (checked by Laraue.Apps.Feedback).
    /// </summary>
    public Dictionary<string, string>? Context { get; init; }
}

public record SendFeedbackResponse
{
    public required long FeedbackId { get; init; }
}
