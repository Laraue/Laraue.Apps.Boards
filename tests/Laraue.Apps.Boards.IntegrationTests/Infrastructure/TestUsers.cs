using System.Collections.Concurrent;
using Laraue.Apps.Identity.Internal.Contracts;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

/// <summary>
/// What a test sets up for a user created with <c>CreateUser</c>. Boards keeps no name/color on the user
/// itself: the name is what the mocked Laraue.Apps.Identity client answers, the color what the test
/// organization builder gives the user as a member (see <see cref="TestUsers"/>).
/// </summary>
public sealed class TestUser
{
    public long? TelegramId { get; set; }

    /// <summary>Empty means "Unknown", like a profile with no names.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Empty means a random palette color.</summary>
    public string Color { get; set; } = string.Empty;
}

/// <summary>
/// The test users' names and colors: the mocked Identity client's <c>GetUserProfile</c> answers from
/// here, and so does the test organization builder when it adds the user as a member. A user nobody
/// registered - e.g. one signed up through the Telegram/Google flows with a fresh global id - is
/// "Test User".
/// </summary>
public static class TestUsers
{
    private static readonly ConcurrentDictionary<Guid, GetUserProfileResponse> ProfilesByGlobalUserId = new();
    private static readonly ConcurrentDictionary<Guid, string> ColorsByUserId = new();

    public static void Register(Guid userId, Guid globalUserId, TestUser user)
    {
        var displayName = user.DisplayName.Length > 0 ? user.DisplayName : "Unknown";
        var initials = (displayName.Length > 1 ? displayName[..2] : displayName).ToUpperInvariant();

        ProfilesByGlobalUserId[globalUserId] = new GetUserProfileResponse { DisplayName = displayName, Initials = initials };

        if (user.Color.Length > 0)
            ColorsByUserId[userId] = user.Color;
    }

    public static GetUserProfileResponse GetIdentityProfile(string globalUserId)
    {
        return ProfilesByGlobalUserId.TryGetValue(Guid.Parse(globalUserId), out var profile)
            ? profile
            : new GetUserProfileResponse { DisplayName = "Test User", Initials = "TU" };
    }

    /// <summary>The color the test gave the user, if any.</summary>
    public static string? GetColor(Guid userId) => ColorsByUserId.GetValueOrDefault(userId);
}
