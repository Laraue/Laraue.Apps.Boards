using System.Collections.Concurrent;
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
    /// <summary>
    /// Profiles saved through <see cref="UpdateUserProfileAsync"/>, kept in memory (the client is a
    /// singleton) so an edit shows up locally until the host restarts.
    /// </summary>
    private readonly ConcurrentDictionary<string, GetUserProfileResponse> _profiles = new();

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

    /// <summary>
    /// There's no real profile to read locally - a user is "Local User" until they edit their profile.
    /// </summary>
    public override AsyncUnaryCall<GetUserProfileResponse> GetUserProfileAsync(
        GetUserProfileRequest request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        return new AsyncUnaryCall<GetUserProfileResponse>(
            Task.FromResult(_profiles.GetValueOrDefault(request.UserId)
                ?? new GetUserProfileResponse { DisplayName = "Local User", Initials = "LU" }),
            Task.FromResult(new Metadata()),
            () => global::Grpc.Core.Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }

    /// <summary>
    /// Stores the profile in memory, with initials derived the way Identity derives them.
    /// </summary>
    public override AsyncUnaryCall<GetUserProfileResponse> UpdateUserProfileAsync(
        UpdateUserProfileRequest request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        var response = new GetUserProfileResponse
        {
            DisplayName = request.DisplayName,
            Initials = UserInitials.FromDisplayName(request.DisplayName),
        };
        if (request.HasGivenName)
            response.GivenName = request.GivenName;
        if (request.HasFamilyName)
            response.FamilyName = request.FamilyName;

        _profiles[request.UserId] = response;

        return new AsyncUnaryCall<GetUserProfileResponse>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => global::Grpc.Core.Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
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
