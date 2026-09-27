using Laraue.Apps.Boards.Services.Members;
using Laraue.Apps.Identity.Internal.Contracts;

namespace Laraue.Apps.Boards.Services.Identity;

public static class UserIdentityServiceClientExtensions
{
    /// <summary>
    /// A new member's profile: the display name and initials from the user's Laraue.Apps.Identity
    /// profile (the source of truth for it), and a random palette color. Lets any failure (including
    /// <see cref="Grpc.Core.RpcException"/>) propagate - call it outside a database transaction.
    /// </summary>
    public static async Task<MemberProfile> GetNewMemberProfileAsync(
        this UserIdentityService.UserIdentityServiceClient client,
        Guid globalUserId,
        CancellationToken cancellationToken)
    {
        var profile = await client.GetUserProfileAsync(
            new GetUserProfileRequest { UserId = globalUserId.ToString() },
            cancellationToken: cancellationToken);

        return new MemberProfile(profile.DisplayName, profile.Initials, Palette.RandomColor());
    }
}
