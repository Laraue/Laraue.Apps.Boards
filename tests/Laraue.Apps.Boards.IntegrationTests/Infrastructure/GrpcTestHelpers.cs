using Grpc.Core;

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
}
