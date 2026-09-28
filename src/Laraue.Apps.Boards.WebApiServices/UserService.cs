using System.ComponentModel.DataAnnotations;
using Grpc.Core;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.Boards.WebApiServices;

public interface IUserService
{
    Task UpdateEpicSortOrder(
        Guid userId,
        EpicSortOrder epicSortOrder,
        CancellationToken cancellationToken = default);
    
    Task<UserDto> GetUser(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The user's global profile, read from Laraue.Apps.Identity.
    /// </summary>
    Task<UserProfileDto> GetProfile(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the user's global profile in Laraue.Apps.Identity. Organizations the user is already a
    /// member of keep their own copy of the name - the new one is used by organizations joined later.
    /// </summary>
    Task<UserProfileDto> UpdateProfile(
        Guid userId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken);
}

public class UserService(
    ICoreUserService coreService,
    DatabaseContext context,
    UserIdentityService.UserIdentityServiceClient identityClient,
    ILogger<UserService> logger)
    : IUserService
{
    public Task UpdateEpicSortOrder(
        Guid userId,
        EpicSortOrder epicSortOrder,
        CancellationToken cancellationToken = default)
    {
        return coreService
            .UpdatePreferences(
                userId,
                update => update.SetProperty(p => p.EpicSortOrder, epicSortOrder),
                cancellationToken);
    }

    public async Task<UserDto> GetUser(Guid userId, CancellationToken cancellationToken)
    {
        var data = await context.ActiveUsers()
            .Where(x => x.Id == userId)
            .Select(x => new
            {
                x.GlobalUserId,
                User = new UserDto
                {
                    TelegramId = x.TelegramId,
                    HasGoogleAccount = x.GoogleSubject != null,
                    Palette = Palette.Colors
                },
            })
            .FirstOrThrowNotFoundEFAsync("User is not found", cancellationToken);

        var user = data.User;
        user.Initials = await GetInitialsAsync(data.GlobalUserId, cancellationToken);
        user.Preferences = await coreService.GetPreferences(userId, cancellationToken);
        user.LanguageCode = user.Preferences.InterfaceLanguage;

        return user;
    }

    public async Task<UserProfileDto> GetProfile(Guid userId, CancellationToken cancellationToken)
    {
        var globalUserId = await GetGlobalUserIdAsync(userId, cancellationToken);

        var profile = await CallIdentityAsync(
            globalUserId,
            () => identityClient.GetUserProfileAsync(
                new GetUserProfileRequest { UserId = globalUserId.ToString() },
                cancellationToken: cancellationToken));

        return ToUserProfileDto(profile);
    }

    public async Task<UserProfileDto> UpdateProfile(
        Guid userId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var globalUserId = await GetGlobalUserIdAsync(userId, cancellationToken);

        var identityRequest = new UpdateUserProfileRequest
        {
            UserId = globalUserId.ToString(),
            DisplayName = request.DisplayName,
        };
        if (request.GivenName is not null)
            identityRequest.GivenName = request.GivenName;
        if (request.FamilyName is not null)
            identityRequest.FamilyName = request.FamilyName;

        var profile = await CallIdentityAsync(
            globalUserId,
            () => identityClient.UpdateUserProfileAsync(identityRequest, cancellationToken: cancellationToken));

        return ToUserProfileDto(profile);
    }

    private Task<Guid> GetGlobalUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return context.ActiveUsers()
            .Where(x => x.Id == userId)
            .Select(x => x.GlobalUserId)
            .FirstOrThrowNotFoundEFAsync("User is not found", cancellationToken);
    }

    /// <summary>
    /// Calls Identity outside of any database transaction. The request is validated before the call,
    /// so Identity rejecting it is unexpected - every failure is reported as the profile service being
    /// unavailable.
    /// </summary>
    private async Task<GetUserProfileResponse> CallIdentityAsync(
        Guid globalUserId,
        Func<AsyncUnaryCall<GetUserProfileResponse>> call)
    {
        try
        {
            return await call();
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Could not read or update the profile of user {GlobalUserId} in Identity", globalUserId);
            throw new ProfileServiceUnavailableException(ErrorMessages.ProfileServiceUnavailable);
        }
    }

    private static UserProfileDto ToUserProfileDto(GetUserProfileResponse profile)
    {
        return new UserProfileDto
        {
            UserName = profile.HasUserName ? profile.UserName : null,
            GivenName = profile.HasGivenName ? profile.GivenName : null,
            FamilyName = profile.HasFamilyName ? profile.FamilyName : null,
            DisplayName = profile.DisplayName,
            Initials = profile.Initials,
        };
    }

    /// <summary>
    /// The initials from the user's Laraue.Apps.Identity profile - the source of truth for it. Null when
    /// Identity can't be reached, so the rest of the user still loads.
    /// </summary>
    private async Task<string?> GetInitialsAsync(Guid globalUserId, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await identityClient.GetUserProfileAsync(
                new GetUserProfileRequest { UserId = globalUserId.ToString() },
                cancellationToken: cancellationToken);

            return profile.Initials;
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Could not read the profile of user {GlobalUserId} from Identity", globalUserId);
            return null;
        }
    }
}

public class UserDto
{
    public long? TelegramId { get; set; }
    public bool HasGoogleAccount { get; set; }

    /// <summary>
    /// The initials from the user's global profile; null when the profile couldn't be read. How the user
    /// is shown in an organization is that organization's <c>memberProfile</c>.
    /// </summary>
    public string? Initials { get; set; }

    public string LanguageCode { get; set; } = string.Empty;
    public required string[] Palette { get; set; }
    public UserPreferencesResponse Preferences { get; set; } = null!;
}

/// <summary>
/// The user's global profile, kept in Laraue.Apps.Identity.
/// </summary>
public class UserProfileDto
{
    /// <summary>
    /// The Telegram username the user signed up with; not editable.
    /// </summary>
    public string? UserName { get; set; }

    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public required string DisplayName { get; set; }

    /// <summary>
    /// Derived by Identity from <see cref="DisplayName"/>.
    /// </summary>
    public required string Initials { get; set; }
}

public class UpdateProfileRequest
{
    /// <summary>
    /// Null or blank clears the name.
    /// </summary>
    [MaxLength(128)]
    public string? GivenName { get; set; }

    /// <inheritdoc cref="GivenName"/>
    [MaxLength(128)]
    public string? FamilyName { get; set; }

    /// <summary>
    /// Stored as given; the initials are derived from it.
    /// </summary>
    [Required]
    [MaxLength(257)]
    public string DisplayName { get; set; } = string.Empty;
}
