using Grpc.Core;
using Laraue.Apps.Feedback.Internal.Contracts;

namespace Laraue.Apps.Boards.Services.Feedback;

/// <summary>
/// Stands in for the real gRPC-backed <see cref="FeedbackService.FeedbackServiceClient"/> when running
/// locally without a live Laraue.Apps.Feedback instance (see "MockExternalServices" in
/// <c>WebApplicationBuilderExtensions.AddFeedbackClient</c>) - accepts every submission and returns an
/// increasing id instead of storing it.
/// </summary>
public class FakeFeedbackServiceClient : FeedbackService.FeedbackServiceClient
{
    private long _lastFeedbackId;

    public override AsyncUnaryCall<SubmitFeedbackResponse> SubmitFeedbackAsync(
        SubmitFeedbackRequest request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        var response = new SubmitFeedbackResponse { FeedbackId = Interlocked.Increment(ref _lastFeedbackId) };

        return new AsyncUnaryCall<SubmitFeedbackResponse>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => global::Grpc.Core.Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }
}
