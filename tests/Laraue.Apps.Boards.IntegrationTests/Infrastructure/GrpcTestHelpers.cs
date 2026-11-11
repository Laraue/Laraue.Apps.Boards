using Grpc.Core;
using Laraue.Apps.Identity.Internal.Contracts;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public static class GrpcTestHelpers
{
    /// <summary>
    /// Wraps <paramref name="response"/> in a completed, successful <see cref="AsyncUnaryCall{TResponse}"/> -
    /// the return shape a mocked gRPC client method (e.g.
    /// <c>UserIdentityServiceClient.CreateUserIfNotExistsAsync</c>) needs to hand back via
    /// <c>Mock.Setup(...).Returns(...)</c>.
    /// </summary>
    public static AsyncUnaryCall<TResponse> AsyncUnaryCallOf<TResponse>(TResponse response) => new(
        Task.FromResult(response),
        Task.FromResult(new Metadata()),
        () => Status.DefaultSuccess,
        () => new Metadata(),
        () => { });

    /// <summary>
    /// What the mocked Identity answers a create call with: a fresh global user id and the source "stored" for the
    /// service - the sent one, trimmed and lower-cased, a stand-in for Identity's own normalization (BRD-295).
    /// </summary>
    public static AsyncUnaryCall<CreateUserIfNotExistsResponse> CreatedUser(string? sentSource)
    {
        var response = new CreateUserIfNotExistsResponse { UserId = Guid.NewGuid().ToString() };
        if (!string.IsNullOrWhiteSpace(sentSource))
            response.Source = sentSource.Trim().ToLowerInvariant();

        return AsyncUnaryCallOf(response);
    }
}
