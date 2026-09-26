using Grpc.Core;
using Laraue.Apps.Identity.Internal.Contracts;

namespace Laraue.Apps.Boards.Services.Identity;

/// <summary>
/// Stands in for the real gRPC-backed <see cref="UserIdentityService.UserIdentityServiceClient"/>
/// when running locally without a live Laraue.Apps.Identity instance (see "MockExternalServices"
/// in <c>WebApplicationBuilderExtensions.AddCoreServices</c>) - always mints a fresh global id
/// instead of actually resolving/creating one. Subclasses the generated client the same way the
/// integration tests' <c>Mock&lt;UserIdentityService.UserIdentityServiceClient&gt;</c> does, just
/// without Moq (which has no place in a non-test project).
/// </summary>
public class FakeUserIdentityServiceClient : UserIdentityService.UserIdentityServiceClient
{
    public override AsyncUnaryCall<CreateUserIfNotExistsResponse> CreateUserIfNotExistsAsync(
        CreateUserIfNotExistsRequest request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateUserIfNotExistsResponse { UserId = Guid.NewGuid().ToString() };

        return new AsyncUnaryCall<CreateUserIfNotExistsResponse>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => global::Grpc.Core.Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }

    public override AsyncUnaryCall<CreateUserIfNotExistsResponse> CreateUserIfNotExistsByGoogleAsync(
        CreateUserIfNotExistsByGoogleRequest request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateUserIfNotExistsResponse { UserId = Guid.NewGuid().ToString() };

        return new AsyncUnaryCall<CreateUserIfNotExistsResponse>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => global::Grpc.Core.Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }

    public override AsyncUnaryCall<LinkAccountResponse> LinkTelegramAccountAsync(
        LinkTelegramAccountRequest request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        return Linked();
    }

    public override AsyncUnaryCall<LinkAccountResponse> LinkGoogleAccountAsync(
        LinkGoogleAccountRequest request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        return Linked();
    }

    private static AsyncUnaryCall<LinkAccountResponse> Linked()
    {
        return new AsyncUnaryCall<LinkAccountResponse>(
            Task.FromResult(new LinkAccountResponse { Result = LinkAccountResult.Linked }),
            Task.FromResult(new Metadata()),
            () => global::Grpc.Core.Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }
}
