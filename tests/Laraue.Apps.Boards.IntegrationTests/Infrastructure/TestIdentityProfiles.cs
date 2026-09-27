using System.Collections.Concurrent;
using Laraue.Apps.Identity.Internal.Contracts;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

/// <summary>
/// What the mocked Laraue.Apps.Identity client answers to <c>GetUserProfile</c>: the name a test user
/// was created with (see <c>CreateUser</c> in the test scopes), or "Test User" for a user nobody
/// registered - e.g. one signed up through the Telegram/Google flows with a fresh global id.
/// </summary>
public static class TestIdentityProfiles
{
    private static readonly ConcurrentDictionary<Guid, GetUserProfileResponse> Profiles = new();

    public static void Set(Guid globalUserId, string displayName, string initials)
    {
        Profiles[globalUserId] = new GetUserProfileResponse { DisplayName = displayName, Initials = initials };
    }

    public static GetUserProfileResponse Get(string globalUserId)
    {
        return Profiles.TryGetValue(Guid.Parse(globalUserId), out var profile)
            ? profile
            : new GetUserProfileResponse { DisplayName = "Test User", Initials = "TU" };
    }
}
